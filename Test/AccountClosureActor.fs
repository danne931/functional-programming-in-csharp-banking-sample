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
            (typed quartzSchedulerProbe :> IActorRef<SchedulingActor.Message>)
            getAccountRef
            (typed emailProbe :> IActorRef<EmailActor.EmailMessage>)
            mockDeleteHistoricalRecords

   accountClosureActor, emailProbe, quartzSchedulerProbe

[<Tests>]
let tests =
   testList "Account Closure Actor" [
      akkaTest "Register should save an account to actor state"
      <| Some config
      <| fun tck ->
         let accountClosureActor, emailProbe, quartzSchedulerProbe = init tck
         let initState = AccountClosureActor.initState

         accountClosureActor <! AccountClosureMessage.GetRegisteredAccounts
         TestKit.expectMsg tck initState |> ignore

         let account1 = {
            Stub.accountState with
               EntityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4")
         }

         let account2 = {
            Stub.accountState with
               EntityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5")
         }

         accountClosureActor <! AccountClosureMessage.Register account1
         accountClosureActor <! AccountClosureMessage.Register account2
         accountClosureActor <! AccountClosureMessage.GetRegisteredAccounts

         let expectedState =
            Map [ account1.EntityId, account1; account2.EntityId, account2 ]

         TestKit.expectMsg tck expectedState |> ignore

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

         let account1 = {
            Stub.accountState with
               EntityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4")
         }

         let account2 = {
            Stub.accountState with
               EntityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5")
         }

         accountClosureActor <! AccountClosureMessage.Register account1
         accountClosureActor <! AccountClosureMessage.Register account2
         accountClosureActor <! AccountClosureMessage.ScheduleDeleteAll

         for account in [ account1; account2 ] do
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
         accountClosureActor <! AccountClosureMessage.GetRegisteredAccounts
         TestKit.expectMsg tck initState |> ignore

      akkaTest
         "ScheduleDeleteAll should schedule a persistent quartz job to
          delete historical records."
      <| Some config
      <| fun tck ->
         let accountClosureActor, _, quartzSchedulerProbe = init tck

         let account1 = {
            Stub.accountState with
               EntityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4")
         }

         let account2 = {
            Stub.accountState with
               EntityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5")
         }

         accountClosureActor <! AccountClosureMessage.Register account1
         accountClosureActor <! AccountClosureMessage.Register account2

         accountClosureActor <! AccountClosureMessage.ScheduleDeleteAll

         let msg = quartzSchedulerProbe.ExpectMsg<SchedulingActor.Message>()

         let expectedAccountIds = [ account1.EntityId; account2.EntityId ]

         match msg with
         | SchedulingActor.DeleteAccountsJobSchedule accountIds ->
            Expect.equal
               accountIds
               expectedAccountIds
               "Quartz job should contain DeleteAll message with account IDs
                of any registered accounts to delete"
         | msg ->
            Expect.isTrue
               false
               $"Expected AccountClosureMessage. Received {msg}"

      akkaTest "ReverseClosure should remove an account from actor state"
      <| Some config
      <| fun tck ->
         let accountClosureActor, emailProbe, quartzSchedulerProbe = init tck
         let initState = AccountClosureActor.initState

         accountClosureActor <! AccountClosureMessage.GetRegisteredAccounts
         TestKit.expectMsg tck initState |> ignore

         let account1 = {
            Stub.accountState with
               EntityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4")
         }

         let account2 = {
            Stub.accountState with
               EntityId = Guid("ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5")
         }

         accountClosureActor <! AccountClosureMessage.Register account1
         accountClosureActor <! AccountClosureMessage.Register account2

         accountClosureActor
         <! AccountClosureMessage.ReverseClosure account1.EntityId

         accountClosureActor <! AccountClosureMessage.GetRegisteredAccounts

         let expectedState = Map [ account2.EntityId, account2 ]

         TestKit.expectMsg tck expectedState |> ignore

         emailProbe.ExpectNoMsg()
         quartzSchedulerProbe.ExpectNoMsg()
   ]
