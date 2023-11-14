module AccountClosureActorTests

open System
open Expecto
open Akka.Actor
open Akkling
open Akkling.Cluster.Sharding
open Akka.Quartz.Actor.Commands
open FsToolkit.ErrorHandling

open Util
open Lib.Types
open ActorUtil
open Bank.Account.Domain

// NOTE: Change default snapshot store from local file system
//       to in memory.
let config =
   Configuration.parse
      """
      akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.inmem"
      """

let initMockAccountActor (tck: TestKit.Tck) =
   let handler (ctx: Actor<_>) (msg: obj) =
      match msg with
      | :? ShardEnvelope as envelope ->
         match envelope.Message with
         | :? AccountMessage as msg ->
            match msg with
            | AccountMessage.Delete ->
               tck.TestActor.Tell AccountMessage.Delete
               ignored ()
            | msg -> unhandled msg
         | msg -> unhandled msg
      | msg -> unhandled msg

   spawn tck "account-mock" <| props (actorOf2 handler)

let mockDeleteHistoricalRecords (_: Guid list) = taskResultOption {
   return [ Email.deserialize "jellyfish@gmail.com" ]
}

let init (tck: TestKit.Tck) =
   let emailProbe = tck.CreateTestProbe()

   let quartzSchedulerProbe = tck.CreateTestProbe()

   let getAccountRef = getAccountEntityRef <| initMockAccountActor tck

   let accountClosureActor =
      spawn tck ActorMetadata.accountClosure.Name
      <| AccountClosureActor.actorProps
            quartzSchedulerProbe
            getAccountRef
            (typed emailProbe :> IActorRef<EmailActor.EmailMessage>)
            mockDeleteHistoricalRecords

   accountClosureActor, emailProbe, quartzSchedulerProbe

let accountStub () = {
   Stub.accountState with
      EntityId = Guid.NewGuid()
}

[<Tests>]
let tests =
   testList "Account Closure Actor" [
      akkaTest "Register should save an account to an actor state list"
      <| Some config
      <| fun tck ->
         let accountClosureActor, emailProbe, quartzSchedulerProbe = init tck
         let initState = AccountClosureActor.initState

         accountClosureActor <! AccountClosureActor.Lookup
         TestKit.expectMsg tck initState |> ignore

         let account1 = accountStub ()
         let account2 = accountStub ()
         accountClosureActor <! AccountClosureActor.Register account1
         accountClosureActor <! AccountClosureActor.Register account2
         accountClosureActor <! AccountClosureActor.Lookup
         TestKit.expectMsg tck [ account2; account1 ] |> ignore

         emailProbe.ExpectNoMsg()
         quartzSchedulerProbe.ExpectNoMsg()

      akkaTest
         "ScheduleDeleteAll should interact with AccountActor & EmailActor
          for every account which is registered for deletion. State
          should reset so no accounts registered for deletion"
      <| Some config
      <| fun tck ->
         let accountClosureActor, emailProbe, _ = init tck
         let initState = AccountClosureActor.initState

         let account1 = accountStub ()
         let account2 = accountStub ()
         accountClosureActor <! AccountClosureActor.Register account1
         accountClosureActor <! AccountClosureActor.Register account2
         accountClosureActor <! AccountClosureActor.ScheduleDeleteAll

         for account in [ account2; account1 ] do
            TestKit.expectMsg tck AccountMessage.Delete |> ignore

            match emailProbe.ExpectMsg<EmailActor.EmailMessage>() with
            | EmailActor.EmailMessage.AccountClose acct ->
               Expect.equal
                  acct
                  account
                  "EmailActor should receive AccountClose
                   message containing a registered account to delete"
            | _ ->
               Expect.isTrue false "EmailActor expects an AccountClose message"

         // State should be reset
         accountClosureActor <! AccountClosureActor.Lookup
         TestKit.expectMsg tck initState |> ignore

      akkaTest
         "ScheduleDeleteAll should schedule a persistent quartz job to
          delete historical records."
      <| Some config
      <| fun tck ->
         let accountClosureActor, _, quartzSchedulerProbe = init tck

         let account1 = accountStub ()
         let account2 = accountStub ()
         accountClosureActor <! AccountClosureActor.Register account1
         accountClosureActor <! AccountClosureActor.Register account2

         accountClosureActor <! AccountClosureActor.ScheduleDeleteAll

         let msg = quartzSchedulerProbe.ExpectMsg<CreatePersistentJob>()

         Expect.equal
            msg.To
            ActorMetadata.accountClosure.Path.Value
            "Quartz job should contain actor path to AccountClosureActor for
             which it will send the scheduled DeleteAll message"

         let expectedAccountIds = [ account2.EntityId; account1.EntityId ]

         let message: AccountClosureActor.AccountClosureMessage =
            unbox msg.Message

         match message with
         | AccountClosureActor.AccountClosureMessage.DeleteAll accountIds ->
            Expect.equal
               accountIds
               expectedAccountIds
               "Quartz job should contain DeleteAll message with account IDs
                of any registered accounts to delete"
         | msg ->
            Expect.isTrue
               false
               $"Expected AccountClosureMessage. Received {msg}"
   ]
