module AccountActorTests

open System.Threading.Tasks
open Expecto
open Akkling
open Akka.Actor

open Util
open Lib.Types
open ActorUtil
open Bank.Account.Domain
open Bank.Transfer.Domain
open User

// NOTE: Change default snapshot store from local file system
//       to in memory.
let config =
   Configuration.parse
      """
      akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.inmem"
      """

let accountPersistence: AccountPersistence = {
   getEvents = fun _ -> Stub.accountEvents |> Some |> Task.FromResult
}

let userPersistence: UserPersistence = {
   createUser = fun _ -> 1 |> Ok |> Task.FromResult
}

let init (tck: TestKit.Tck) =
   let internalTransferProbe = tck.CreateTestProbe()
   let domesticTransferProbe = tck.CreateTestProbe()
   let emailProbe = tck.CreateTestProbe()
   let accountClosureProbe = tck.CreateTestProbe()
   let billingCycleProbe = tck.CreateTestProbe()

   let getOrStartInternalTransferActor (_: Actor<_>) =
      (typed internalTransferProbe :> IActorRef<BankEvent<TransferPending>>)

   let getDomesticTransferActor (_: ActorSystem) =
      (typed domesticTransferProbe
      :> IActorRef<DomesticTransferRecipientActor.Message>)

   let getEmailActor (_: ActorSystem) =
      (typed emailProbe :> IActorRef<EmailActor.EmailMessage>)

   let getAccountClosureActor (_: ActorSystem) =
      (typed accountClosureProbe
      :> IActorRef<AccountClosureActor.AccountClosureMessage>)

   let startBillingCycleActor (_: Actor<_>) (_: AccountState) =
      (typed billingCycleProbe :> IActorRef<BillingCycleActor.Message>)

   let accountActor =
      spawn tck ActorMetadata.account.Name
      <| AccountActor.actorProps
            accountPersistence
            Stub.accountBroadcast
            getOrStartInternalTransferActor
            getDomesticTransferActor
            getEmailActor
            getAccountClosureActor
            startBillingCycleActor
            userPersistence

   {|
      accountActor = accountActor
      internalTransferProbe = internalTransferProbe
      domesticTransferProbe = domesticTransferProbe
      emailProbe = emailProbe
      accountClosureProbe = accountClosureProbe
      billingCycleProbe = billingCycleProbe
   |}

[<Tests>]
let tests =
   testList "Account Actor" [
      akkaTest
         "Init account should save the account, create a user, & send a welcome email"
      <| Some config
      <| fun tck ->
         let o = init tck
         o.accountActor <! StateChange Stub.command.createAccount
         o.accountActor <! Lookup

         let state = tck.ExpectMsg<Option<AccountState>>()

         Expect.equal
            state
            (Some Stub.accountStateAfterCreate)
            "Account state after CreateAccountCommand should be initialized"

         let msg = o.emailProbe.ExpectMsg<EmailActor.EmailMessage>()

         match msg with
         | EmailActor.EmailMessage.AccountOpen account ->
            Expect.equal
               account
               Stub.accountStateAfterCreate
               "EmailActor should receive AccountOpen message with created
               account"
         | msg ->
            Expect.isTrue
               false
               $"Expected AccountOpen EmailMessage.  
            Received {msg}"

      akkaTest "Close account should interact with the AccountClosureActor"
      <| Some config
      <| fun tck ->
         let o = init tck
         o.accountActor <! StateChange Stub.command.createAccount
         o.accountActor <! StateChange Stub.command.closeAccount
         o.accountActor <! Lookup

         let expectedState = {
            Stub.accountStateAfterCreate with
               Status = AccountStatus.Closed
         }

         let state = tck.ExpectMsg<Option<AccountState>>()

         Expect.equal
            state
            (Some expectedState)
            "Account state should be closed"

         let msg =
            o.accountClosureProbe.ExpectMsg<AccountClosureActor.AccountClosureMessage>
               ()

         match msg with
         | AccountClosureActor.AccountClosureMessage.Register account ->
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
         "A failed debit due to insufficient balance should notify the EmailActor"
      <| Some config
      <| fun tck ->
         let o = init tck
         o.accountActor <! StateChange Stub.command.createAccount

         let debit =
            Stub.command.debit <| Stub.accountStateAfterCreate.Balance + 1m

         o.accountActor <! StateChange debit
         o.accountActor <! Lookup

         let state = tck.ExpectMsg<Option<AccountState>>()

         Expect.equal
            state
            (Some Stub.accountStateAfterCreate)
            "Account state should be unchanged after failed debit validation due
            to insufficient balance"

         let msg = o.emailProbe.ExpectMsg<EmailActor.EmailMessage>()

         match msg with
         | EmailActor.EmailMessage.DebitDeclined(errMsg, account) ->
            Expect.equal
               account
               Stub.accountStateAfterCreate
               "EmailActor should receive a DebitDeclined message for
               insufficient balance"

            Expect.stringContains
               errMsg
               "InsufficientBalance"
               "DebitDeclined message sent due to InsufficientBalance"
         | msg ->
            Expect.isTrue
               false
               $"Expected DebitDeclined EmailMessage. Received {msg}"

      akkaTest
         "A failed debit due to exceeding daily debit allowance should notify the EmailActor"
      <| Some config
      <| fun tck ->
         let o = init tck
         o.accountActor <! StateChange Stub.command.createAccount
         o.accountActor <! StateChange(Stub.command.limitDailyDebits 100m)
         o.accountActor <! Lookup

         let expectedState = {
            Stub.accountStateAfterCreate with
               DailyDebitLimit = 100m
         }

         let state = tck.ExpectMsg<Option<AccountState>>()

         Expect.equal
            state
            (Some expectedState)
            "Account state should be configured with the daily debit limit"

         let debit1 = Stub.command.debit 98m
         let debit2 = Stub.command.debit 33m
         o.accountActor <! StateChange debit1
         o.accountActor <! StateChange debit2
         o.accountActor <! Lookup
         let state = tck.ExpectMsg<Option<AccountState>>()

         o.emailProbe.ExpectMsg<EmailActor.EmailMessage>() |> ignore
         let msg = o.emailProbe.ExpectMsg<EmailActor.EmailMessage>()

         match msg with
         | EmailActor.EmailMessage.DebitDeclined(errMsg, account) ->
            Expect.equal
               (Some account)
               state
               "EmailActor should receive a DebitDeclined message for exceeding
               daily debits"

            Expect.stringContains
               errMsg
               "ExceededDailyDebit"
               "DebitDeclined message sent due to ExceededDailyDebit"
         | msg ->
            Expect.isTrue
               false
               $"Expected DebitDeclined EmailMessage. Received {msg}"

      akkaTest
         "An internal transfer should message the InternalTransferRecipientActor"
      <| Some config
      <| fun tck ->
         let o = init tck
         o.accountActor <! StateChange Stub.command.createAccount
         let transfer = Stub.command.internalTransfer 33m
         o.accountActor <! StateChange Stub.command.registerInternalRecipient
         o.accountActor <! StateChange transfer
         o.accountActor <! Lookup

         let state = tck.ExpectMsg<Option<AccountState>>()

         Expect.isTrue state.IsSome "account state is not None"

         Expect.equal
            state.Value.Balance
            (Stub.accountStateAfterCreate.Balance - transfer.Amount)
            "Account state should reflect a transfer debit"

         o.internalTransferProbe.ExpectMsg<BankEvent<TransferPending>>()
         |> ignore

      akkaTest
         "A domestic transfer should message the DomesticTransferRecipientActor"
      <| Some config
      <| fun tck ->
         let o = init tck
         o.accountActor <! StateChange Stub.command.createAccount
         let transfer = Stub.command.domesticTransfer 31m
         o.accountActor <! StateChange Stub.command.registerDomesticRecipient
         o.accountActor <! StateChange transfer
         o.accountActor <! Lookup

         let state = tck.ExpectMsg<Option<AccountState>>()

         Expect.isTrue state.IsSome "account state is not None"

         Expect.equal
            state.Value.Balance
            (Stub.accountStateAfterCreate.Balance - transfer.Amount)
            "Account state should reflect a transfer debit"

         let msg =
            o.domesticTransferProbe.ExpectMsg<DomesticTransferRecipientActor.Message>
               ()

         match msg with
         | DomesticTransferRecipientActor.TransferPending(evt) ->
            Expect.isTrue true ""
         | msg ->
            Expect.isTrue
               false
               $"Expected TransferPending DomesticTransfer Message. Received {msg}"

      akkaTest "Receiving a transfer deposit should notify the EmailActor"
      <| Some config
      <| fun tck ->
         let o = init tck
         o.accountActor <! StateChange Stub.command.createAccount
         let deposit = Stub.command.depositTransfer 101m
         o.accountActor <! StateChange deposit
         o.accountActor <! Lookup

         let expectedState = {
            Stub.accountStateAfterCreate with
               Balance = Stub.accountStateAfterCreate.Balance + deposit.Amount
         }

         let state = tck.ExpectMsg<Option<AccountState>>()

         Expect.equal
            state
            (Some expectedState)
            "Account state should reflect a received transfer"

         match o.emailProbe.ExpectMsg<EmailActor.EmailMessage>() with
         | EmailActor.EmailMessage.TransferDeposited(evt, account) ->
            Expect.equal
               (Some account)
               state
               "EmailActor should receive a TransferDeposited message"
         | msg ->
            Expect.isTrue
               false
               $"Expected TransferDeposited EmailMessage. Received {msg}"
   ]
