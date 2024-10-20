module AccountClosureActorTests

open System
open Expecto
open Akka.Actor
open Akkling
open Akkling.Cluster.Sharding
open FsToolkit.ErrorHandling

open Util
open Lib.SharedTypes
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

let mockDeleteHistoricalRecords (_: AccountId list) = taskResultOption {
   let accountNum = AccountNumber.generate () |> int64 |> AccountNumber
   return [ accountNum ]
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
         (fun _ -> (typed emailProbe :> IActorRef<EmailActor.EmailMessage>))
         mockDeleteHistoricalRecords
         {
            Count = 10
            Burst = 10
            Duration = TimeSpan.FromSeconds 3
         }

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
               AccountId =
                  "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4"
                  |> Guid.Parse
                  |> AccountId
         }

         let account2 = {
            Stub.accountState with
               AccountId =
                  "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5"
                  |> Guid.Parse
                  |> AccountId
         }

         accountClosureActor <! AccountClosureMessage.Register account1
         accountClosureActor <! AccountClosureMessage.Register account2
         accountClosureActor <! AccountClosureMessage.GetRegisteredAccounts

         let expectedState =
            Map [ account1.AccountId, account1; account2.AccountId, account2 ]

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
               AccountId =
                  "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4"
                  |> Guid.Parse
                  |> AccountId
         }

         let account2 = {
            Stub.accountState with
               AccountId =
                  "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5"
                  |> Guid.Parse
                  |> AccountId
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
               AccountId =
                  "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a4"
                  |> Guid.Parse
                  |> AccountId
         }

         let account2 = {
            Stub.accountState with
               AccountId =
                  "ec3e94cc-eba1-4ff4-b3dc-55010ecf67a5"
                  |> Guid.Parse
                  |> AccountId
         }

         accountClosureActor <! AccountClosureMessage.Register account1
         accountClosureActor <! AccountClosureMessage.Register account2
         accountClosureActor <! AccountClosureMessage.ScheduleDeleteAll

         let msg = quartzSchedulerProbe.ExpectMsg<SchedulingActor.Message>()

         let expectedAccountIds = [ account1.AccountId; account2.AccountId ]

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
   ]
