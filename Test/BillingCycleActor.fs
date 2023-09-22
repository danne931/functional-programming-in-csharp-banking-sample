module BillingCycleActorTests

open System.Threading.Tasks
open Expecto
open Akkling
open Akkling.TestKit

module BCActor = BillingCycleActor
module BCBWActor = BillingCycleBulkWriteActor

open Util
open BankTypes
open Bank.Account.Domain

let accountPersistence: AccountPersistence = {
   getEvents = fun _ -> Stub.transactions |> Some |> Task.FromResult
}

let accountPersistenceNoTransactions: AccountPersistence = {
   getEvents = fun _ -> None |> Task.FromResult
}

let accountStub = {
   AccountState.empty with
      Balance = Stub.billingStatement.Balance
      EntityId = Stub.billingStatement.AccountId
}

let accountStubSkipMaintenanceFee = {
   accountStub with
      MaintenanceFeeCriteria = {
         QualifyingDepositFound = true
         DailyBalanceThreshold = false
      }
}

let init
   (tck: TestKit.Tck)
   (persistence: AccountPersistence)
   (accountStub: AccountState)
   =
   let emailProbe = tck.CreateTestProbe()
   let billingBulkProbe = tck.CreateTestProbe()

   let billingProps =
      BCActor.actorProps
         persistence
         accountStub
         (typed billingBulkProbe
         :> IActorRef<BillingCycleBulkWriteActor.Message>)
         (typed emailProbe :> IActorRef<EmailActor.EmailMessage>)

   // Using ChildActorOf so messages intended for BillingCycleActor
   // parent (AccountActor) can be observed from tests on tck.
   let billingCycleActor =
      tck.ChildActorOf <| billingProps.ToProps() |> typed
      :> IActorRef<BillingCycleActor.Message>

   billingCycleActor, emailProbe, billingBulkProbe

[<Tests>]
let tests =
   testList "Billing Cycle Actor" [
      akkaTest
         "BillingCycleActor immediately shuts down if no account transactions
          found for a billing cycle"
      <| None
      <| fun tck ->
         let billingCycleActor, emailProbe, billingBulkProbe =
            init tck accountPersistenceNoTransactions accountStub

         monitor tck billingCycleActor

         billingCycleActor <! BCActor.Message.SaveBillingStatement

         emailProbe.ExpectNoMsg()
         billingBulkProbe.ExpectNoMsg()
         expectTerminated tck billingCycleActor |> ignore

      akkaTest
         "BillingCycleActor interacts with BillingCycleBulkWriteActor, EmailActor, &
          parent Account Actor when account transactions for a billing cycle found."
      <| None
      <| fun tck ->
         let billingCycleActor, emailProbe, billingBulkProbe =
            init tck accountPersistence accountStub

         monitor tck billingCycleActor

         billingCycleActor <! BCActor.Message.SaveBillingStatement

         match billingBulkProbe.ExpectMsg<BCBWActor.Message>() with
         | BCBWActor.RegisterBillingStatement statement ->
            Expect.sequenceEqual
               statement.Transactions
               Stub.transactions
               "RegisterBillingStatements msg should send transactions equivalent
                to those associated with the account events"

            Expect.equal
               statement.Balance
               accountStub.Balance
               "Billing statement balance should = account balance"

            Expect.equal
               statement.AccountId
               accountStub.EntityId
               "Billing statement AccountId should = account EntityId"
         | msg ->
            Expect.isTrue
               false
               $"BillingCycleBulkWriteActor expects RegisterBillingStatement
                 message. Received message: {msg}"

         expectMsg tck AccountMessage.BillingCycleEnd |> ignore

         match tck.ExpectMsg<AccountMessage>() with
         | StateChange command ->
            let isSameType = typeof<MaintenanceFeeCommand> = command.GetType()

            Expect.isTrue
               isSameType
               "Parent of billing cycle actor (account actor)
                should receive a MaintenanceFeeCommand"
         | msg -> Expect.isTrue false $"Received incorrect message {msg}"

         match emailProbe.ExpectMsg<EmailActor.EmailMessage>() with
         | EmailActor.BillingStatement account ->
            Expect.equal
               account
               accountStub
               "EmailActor BillingStatement message should contain
                an account record equivalent to the one provided
                to BillingCycleActor"
         | msg ->
            Expect.isTrue
               false
               $"EmailActor expects BillingStatement message.
                 Received message: {msg}"

         expectTerminated tck billingCycleActor |> ignore

      akkaTest
         "BillingCycleActor should skip sending a MaintenanceFeeCommand when
          maintenance fee criteria met."
      <| None
      <| fun tck ->
         let billingCycleActor, _, _ =
            init tck accountPersistence accountStubSkipMaintenanceFee

         monitor tck billingCycleActor

         billingCycleActor <! BCActor.Message.SaveBillingStatement

         expectMsg tck AccountMessage.BillingCycleEnd |> ignore

         match tck.ExpectMsg<AccountMessage>() with
         | StateChange command ->
            let isSameType =
               typeof<SkipMaintenanceFeeCommand> = command.GetType()

            Expect.isTrue
               isSameType
               "Parent of billing cycle actor (account actor)
                should receive a SkipMaintenanceFeeCommand"
         | msg -> Expect.isTrue false $"Received incorrect message {msg}"

         expectTerminated tck billingCycleActor |> ignore
   ]
