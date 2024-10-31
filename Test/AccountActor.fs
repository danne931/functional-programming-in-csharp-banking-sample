module AccountActorTests

open System
open Expecto
open Akkling
open Akka.Actor
open Akka.Persistence.Extras
open Akkling.Cluster.Sharding

open Util
open ActorUtil
open Bank.Account.Domain
open Bank.Employee.Domain
open Bank.Transfer.Domain
open BillingStatement

module Stub = AccountStub

type private InternalTransferMsg =
   InternalTransferRecipientActor.InternalTransferMessage

type private DomesticTransferMsg =
   DomesticTransferRecipientActor.DomesticTransferMessage

// NOTE: Change default snapshot store from local file system
//       to in memory.
let config =
   Configuration.parse
      """
      akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.inmem"
      """

let initialDepositCommand amount =
   AccountCommand.DepositCash <| Stub.command.depositCash amount


// Mock PersistenceSupervisor message wrapping for command
// intended to be persisted
let envelope msg =
   ConfirmableMessageEnvelope(Int64.MinValue, "", msg)

// Mock PersistenceSupervisor forwards messages to AccountActor
// & wraps StateChange messages intended to be persisted.
let mockPersistenceSupervisorProps
   (spawnChild: Actor<AccountMessage> -> IActorRef<obj>)
   =
   let init (ctx: Actor<AccountMessage>) =
      let child = spawnChild ctx

      actor {
         let! msg = ctx.Receive()

         if AccountActor.isPersistableMessage msg then
            child <<! envelope msg
         else
            child <<! msg

         return ignored ()
      }

   props init

let initMockEmployeeActor (tck: TestKit.Tck) =
   let handler (ctx: Actor<_>) (msg: obj) =
      match msg with
      | :? ShardEnvelope as envelope ->
         match envelope.Message with
         | :? EmployeeMessage as msg ->
            match msg with
            | EmployeeMessage.StateChange(EmployeeCommand.DeclineDebit cmd) ->
               cmd
               |> EmployeeCommand.DeclineDebit
               |> EmployeeMessage.StateChange
               |> tck.TestActor.Tell
               |> ignored
            | msg -> unhandled msg
         | msg -> unhandled msg
      | msg -> unhandled msg

   spawn tck "employee-mock" <| props (actorOf2 handler)

let init (tck: TestKit.Tck) =
   let internalTransferProbe = tck.CreateTestProbe()
   let domesticTransferProbe = tck.CreateTestProbe()
   let emailProbe = tck.CreateTestProbe()
   let accountClosureProbe = tck.CreateTestProbe()
   let billingProbe = tck.CreateTestProbe()
   let accountProbe = tck.CreateTestProbe()
   let schedulingProbe = tck.CreateTestProbe()

   let getEmployeeRef = getEmployeeEntityRef (initMockEmployeeActor tck)

   let getAccountRef =
      accountProbe |> typed :> IActorRef<AccountMessage> |> getAccountEntityRef

   let getOrStartInternalTransferActor (_: Actor<_>) =
      typed internalTransferProbe :> IActorRef<InternalTransferMsg>

   let getDomesticTransferActor (_: ActorSystem) =
      typed domesticTransferProbe :> IActorRef<DomesticTransferMsg>

   let getEmailActor (_: ActorSystem) =
      (typed emailProbe :> IActorRef<EmailActor.EmailMessage>)

   let getAccountClosureActor (_: ActorSystem) =
      typed accountClosureProbe :> IActorRef<AccountClosureMessage>

   let getBillingStatementActor (_: ActorSystem) =
      typed billingProbe :> IActorRef<BillingStatementMessage>

   let prop =
      mockPersistenceSupervisorProps (fun ctx ->
         spawn ctx ActorMetadata.account.Name
         <| AccountActor.actorProps
               Stub.accountBroadcast
               getOrStartInternalTransferActor
               getDomesticTransferActor
               getEmailActor
               getAccountClosureActor
               getBillingStatementActor
               getEmployeeRef
               getAccountRef
               (schedulingProbe |> typed :> IActorRef<SchedulingActor.Message>))

   let accountActor = spawn tck ActorMetadata.account.Name prop

   {|
      accountActor = accountActor
      internalTransferProbe = internalTransferProbe
      domesticTransferProbe = domesticTransferProbe
      emailProbe = emailProbe
      accountClosureProbe = accountClosureProbe
      billingProbe = billingProbe
   |}

[<Tests>]
let tests =
   testList "Account Actor" [
      akkaTest
         "Init account should save the account, create a user, & send a welcome email"
      <| Some config
      <| fun tck ->
         let o = init tck
         let cmd = AccountCommand.CreateAccount Stub.command.createAccount
         o.accountActor <! AccountMessage.StateChange cmd
         o.accountActor <! AccountMessage.GetAccount

         let state = tck.ExpectMsg<Option<Account>>()

         Expect.equal
            state
            (Some Stub.accountStateAfterCreate)
            "Account state after CreateAccountCommand should be initialized"

         let msg = o.emailProbe.ExpectMsg<EmailActor.EmailMessage>()

         match msg with
         | EmailActor.EmailMessage.AccountOpen(accountName, orgId) ->
            Expect.equal
               (accountName, orgId)
               (state.Value.FullName, state.Value.OrgId)
               "EmailActor should receive AccountOpen message with created
               account"
         | msg ->
            Expect.isTrue
               false
               $"Expected AccountOpen EmailMessage. Received {msg}"

      akkaTest "Close account should interact with the AccountClosureActor"
      <| Some config
      <| fun tck ->
         let o = init tck
         let cmd = AccountCommand.CreateAccount Stub.command.createAccount
         o.accountActor <! AccountMessage.StateChange cmd

         let cmd = AccountCommand.CloseAccount Stub.command.closeAccount
         o.accountActor <! AccountMessage.StateChange cmd
         o.accountActor <! AccountMessage.GetAccount

         let expectedState = {
            Stub.accountStateAfterCreate with
               Status = AccountStatus.Closed
         }

         let state = tck.ExpectMsg<Option<Account>>()

         Expect.equal
            state
            (Some expectedState)
            "Account state should be closed"

         let msg = o.accountClosureProbe.ExpectMsg<AccountClosureMessage>()

         match msg with
         | AccountClosureMessage.Register account ->
            Expect.equal
               account
               expectedState
               "AccountClosure actor should receive a Register message with an
                account intended to close"
         | msg ->
            Expect.isTrue
               false
               $"Expected Register AccountClosureMessage. Received {msg}"

      akkaTest
         "A failed debit due to insufficient balance should notify the employee actor who is attempting a purchase"
      <| Some config
      <| fun tck ->
         let o = init tck
         let cmd = AccountCommand.CreateAccount Stub.command.createAccount
         o.accountActor <! AccountMessage.StateChange cmd

         o.accountActor
         <! AccountMessage.StateChange(initialDepositCommand 2000m)

         let debit = Stub.command.debit 2001m

         o.accountActor
         <! AccountMessage.StateChange(AccountCommand.Debit debit)

         o.accountActor <! AccountMessage.GetAccount

         let state = tck.ExpectMsg<Option<Account>>()
         let account = Expect.wantSome state ""

         Expect.equal
            account.Balance
            2000m
            "Account state should be unchanged after failed debit validation due
            to insufficient balance"

         let msg = tck.ExpectMsg<EmployeeMessage>()

         match msg with
         | EmployeeMessage.StateChange(EmployeeCommand.DeclineDebit cmd) ->
            Expect.equal
               cmd.Data.Reason
               (PurchaseDeclinedReason.InsufficientAccountFunds(
                  account.Balance,
                  account.FullName
               ))
               "Expect InsufficientAccountFunds purchase declined reason"
         | msg ->
            Expect.isTrue
               false
               $"Expected DebitDeclined EmployeeCommand. Received {msg}"

      akkaTest
         "An internal transfer should message the InternalTransferRecipientActor"
      <| Some config
      <| fun tck ->
         let o = init tck
         let cmd = AccountCommand.CreateAccount Stub.command.createAccount
         o.accountActor <! AccountMessage.StateChange cmd

         o.accountActor
         <! AccountMessage.StateChange(initialDepositCommand 2000m)

         let transfer = Stub.command.internalTransfer 33m
         let cmd = AccountCommand.InternalTransfer transfer
         o.accountActor <! AccountMessage.StateChange cmd
         o.accountActor <! AccountMessage.GetAccount

         let state = tck.ExpectMsg<Option<Account>>()

         Expect.isTrue state.IsSome "account state is not None"

         Expect.equal
            state.Value.Balance
            (2000m - transfer.Data.Amount)
            "Account state should reflect a transfer debit"

         o.internalTransferProbe.ExpectMsg<InternalTransferMsg>() |> ignore

      akkaTest
         "A domestic transfer should message the DomesticTransferRecipientActor"
      <| Some config
      <| fun tck ->
         let o = init tck
         let cmd = AccountCommand.CreateAccount Stub.command.createAccount
         o.accountActor <! AccountMessage.StateChange cmd

         o.accountActor
         <! AccountMessage.StateChange(initialDepositCommand 2000m)

         let msg =
            Stub.command.registerDomesticRecipient
            |> AccountCommand.RegisterDomesticTransferRecipient
            |> AccountMessage.StateChange

         o.accountActor <! msg

         o.accountActor <! AccountMessage.GetAccount
         let state = tck.ExpectMsg<Option<Account>>()
         let account = Expect.wantSome state ""

         let recipientStubIdOverride =
            account.DomesticTransferRecipients.Head().Key

         let transfer = Stub.command.domesticTransfer 31m

         let transfer = {
            transfer with
               Data.Recipient.AccountId = recipientStubIdOverride
         }

         let cmd = AccountCommand.DomesticTransfer transfer

         o.accountActor <! AccountMessage.StateChange cmd
         o.accountActor <! AccountMessage.GetAccount

         let state = tck.ExpectMsg<Option<Account>>()

         Expect.isTrue state.IsSome "account state is not None"

         Expect.equal
            state.Value.Balance
            (2000m - transfer.Data.Amount)
            "Account state should reflect a transfer debit"

         let msg = o.domesticTransferProbe.ExpectMsg<DomesticTransferMsg>()

         match msg with
         | DomesticTransferMsg.TransferRequest(action, evt) ->
            Expect.isTrue true ""
         | msg ->
            Expect.isTrue
               false
               $"Expected TransferPending DomesticTransfer Message. Received {msg}"

      akkaTest "Receiving a transfer deposit should notify the EmailActor"
      <| Some config
      <| fun tck ->
         let o = init tck
         let cmd = AccountCommand.CreateAccount Stub.command.createAccount
         o.accountActor <! AccountMessage.StateChange cmd

         o.accountActor
         <! AccountMessage.StateChange(initialDepositCommand 2000m)

         let deposit = Stub.command.depositTransferBetweenOrgs 101m

         o.accountActor
         <! AccountMessage.StateChange(
            AccountCommand.DepositTransferBetweenOrgs deposit
         )

         o.accountActor <! AccountMessage.GetAccount

         let state = tck.ExpectMsg<Option<Account>>()
         let account = Expect.wantSome state ""

         Expect.equal
            account.Balance
            2101m
            "Account state should reflect a received transfer"

         o.emailProbe.ExpectMsg<EmailActor.EmailMessage>() |> ignore

         match o.emailProbe.ExpectMsg<EmailActor.EmailMessage>() with
         | EmailActor.EmailMessage.InternalTransferBetweenOrgsDeposited info ->
            Expect.isTrue true ""
         | msg ->
            Expect.isTrue
               false
               $"Expected TransferDeposited EmailMessage. Received {msg}"

      akkaTest
         "AccountActor interacts with BillingStatementActor & EmailActor
          when account transactions for a billing cycle found."
      <| Some config
      <| fun tck ->
         let o = init tck

         for command in Stub.commands do
            o.accountActor <! AccountMessage.StateChange command

         o.accountActor <! AccountMessage.GetAccount
         let initAccount = tck.ExpectMsg<Option<Account>>().Value

         let msg =
            StartBillingCycleCommand.create initAccount.CompositeId {
               Month = Stub.billingPeriod.Month
               Year = Stub.billingPeriod.Year
               Reference = None
            }
            |> AccountCommand.StartBillingCycle
            |> AccountMessage.StateChange

         o.accountActor <! msg

         let (RegisterBillingStatement statement) =
            o.billingProbe.ExpectMsg<BillingStatementMessage>()

         Expect.equal
            statement.Balance
            initAccount.Balance
            "Billing statement balance should = account balance"

         Expect.equal
            statement.AccountId
            initAccount.AccountId
            "Billing statement AccountId should = account AccountId"

         let getEventName =
            BillingTransaction.value
            >> AccountEnvelope.unwrap
            >> snd
            >> _.EventName

         Expect.sequenceEqual
            (statement.Transactions |> List.map getEventName)
            (Stub.billingTransactions |> List.map getEventName |> List.rev)
            "RegisterBillingStatements msg should send transactions equivalent
             to those associated with the account events"

         o.emailProbe.ExpectMsg<EmailActor.EmailMessage>() |> ignore

         match o.emailProbe.ExpectMsg<EmailActor.EmailMessage>() with
         | EmailActor.EmailMessage.BillingStatement(accountName, orgId) ->
            Expect.equal
               (accountName, orgId)
               (initAccount.FullName, initAccount.OrgId)
               "EmailActor BillingStatement message should contain
                an account record equivalent to the current account state"
         | msg ->
            Expect.isTrue
               false
               $"EmailActor expects BillingStatement message.
                 Received message: {msg}"

         o.accountActor <! AccountMessage.GetAccount

         let accountAfterBillingCycle = tck.ExpectMsg<Option<Account>>().Value

         Expect.equal
            accountAfterBillingCycle.Balance
            (initAccount.Balance - MaintenanceFee.RecurringDebitAmount)
            "Account balance after billing cycle should be decremented by
             maintenance fee"

      akkaTest
         "Maintenance fee should be skipped when maintenance fee criteria met."
      <| Some config
      <| fun tck ->
         let o = init tck

         for command in Stub.commands do
            o.accountActor <! AccountMessage.StateChange command

         o.accountActor <! AccountMessage.GetAccount

         let initAccount = tck.ExpectMsg<Option<Account>>().Value

         let msg =
            StartBillingCycleCommand.create initAccount.CompositeId {
               Month = Stub.billingPeriod.Month
               Year = Stub.billingPeriod.Year
               Reference = None
            }
            |> AccountCommand.StartBillingCycle
            |> AccountMessage.StateChange

         o.accountActor <! msg

         o.accountActor <! AccountMessage.GetAccount

         let accountAfterBillingCycle = tck.ExpectMsg<Option<Account>>().Value

         Expect.equal
            accountAfterBillingCycle.Balance
            initAccount.Balance
            "Account balance after billing cycle should not change since
             the maintenance fee balance threshold is met."
   ]
