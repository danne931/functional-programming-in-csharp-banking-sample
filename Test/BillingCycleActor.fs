module BillingCycleActorTests

open Expecto
open Akkling
open Akka.Actor
open Akkling.TestKit
open Akkling.Cluster.Sharding

module BCActor = BillingCycleActor
module BCBWActor = BillingCycleBulkWriteActor

open Util
open ActorUtil
open Bank.Account.Domain

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

let initMockAccountActor
   (tck: TestKit.Tck)
   (txnsOpt: AccountEvent list option)
   =
   let handler (ctx: Actor<_>) (msg: obj) =
      match msg with
      | :? ShardEnvelope as envelope ->
         match envelope.Message with
         | :? AccountMessage as msg ->
            match msg with
            | AccountMessage.LookupEvents ->
               ctx.Sender() <! txnsOpt
               ignored ()
            | AccountMessage.BillingCycleEnd ->
               tck.TestActor.Tell msg
               ignored ()
            | AccountMessage.StateChange command ->
               match command with
               | :? MaintenanceFeeCommand
               | :? SkipMaintenanceFeeCommand as cmd ->
                  tck.TestActor.Tell cmd
                  ignored ()
               | msg -> unhandled msg
            | msg -> unhandled msg
         | msg -> unhandled msg
      | msg -> unhandled msg

   spawnAnonymous tck <| props (actorOf2 handler)

let init
   (tck: TestKit.Tck)
   (accountStub: AccountState)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   =
   let emailProbe = tck.CreateTestProbe()
   let billingBulkProbe = tck.CreateTestProbe()

   let billingProps =
      BCActor.actorProps
         accountStub
         (typed billingBulkProbe
         :> IActorRef<BillingCycleBulkWriteActor.Message>)
         (typed emailProbe :> IActorRef<EmailActor.EmailMessage>)
         getAccountRef

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
         let mockAccountRef = initMockAccountActor tck None

         let billingCycleActor, emailProbe, billingBulkProbe =
            init tck accountStub <| getAccountEntityRef mockAccountRef

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
         let mockAccountRef =
            initMockAccountActor tck <| Some Stub.accountEvents

         let billingCycleActor, emailProbe, billingBulkProbe =
            init tck accountStub <| getAccountEntityRef mockAccountRef

         billingCycleActor <! BCActor.Message.SaveBillingStatement

         match billingBulkProbe.ExpectMsg<BCBWActor.Message>() with
         | BCBWActor.RegisterBillingStatement statement ->
            Expect.sequenceEqual
               (statement.Transactions |> List.map (fun k -> k.Name))
               (Stub.billingTransactions |> List.map (fun k -> k.Name))
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
         tck.ExpectMsg<MaintenanceFeeCommand>() |> ignore

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

      akkaTest
         "BillingCycleActor should skip sending a MaintenanceFeeCommand when
          maintenance fee criteria met."
      <| None
      <| fun tck ->
         let mockAccountRef =
            initMockAccountActor tck <| Some Stub.accountEvents

         let billingCycleActor, _, _ =
            init tck accountStubSkipMaintenanceFee
            <| getAccountEntityRef mockAccountRef

         billingCycleActor <! BCActor.Message.SaveBillingStatement

         expectMsg tck AccountMessage.BillingCycleEnd |> ignore
         tck.ExpectMsg<SkipMaintenanceFeeCommand>() |> ignore
   ]
