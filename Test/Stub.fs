[<RequireQualifiedAccess>]
module Stub

open System

open Lib.Types
open Bank.Account.Domain
open Bank.Transfer.Domain
open BillingStatement

let entityId = Guid.NewGuid()
let correlationId = Guid.NewGuid()

let internalRecipient = {
   LastName = "fish"
   FirstName = "big"
   Identification = Guid.NewGuid().ToString()
   IdentificationStrategy = RecipientAccountIdentificationStrategy.AccountId
   Currency = Currency.EUR
   AccountEnvironment = RecipientAccountEnvironment.Internal
   RoutingNumber = None
}

let domesticRecipient = {
   LastName = "fish"
   FirstName = "big"
   Identification = Guid.NewGuid().ToString()
   IdentificationStrategy = RecipientAccountIdentificationStrategy.AccountId
   Currency = Currency.VND
   AccountEnvironment = RecipientAccountEnvironment.Domestic
   RoutingNumber = Some "1992384"
}

let command = {|
   createAccount =
      CreateAccountCommand(
         entityId,
         email = "smallfish@gmail.com",
         balance = 2000m,
         firstName = "small",
         lastName = "fish",
         currency = Currency.VND,
         correlationId = correlationId
      )
   closeAccount = CloseAccountCommand(entityId, reference = "")
   debit =
      fun amount ->
         DebitCommand(
            entityId,
            DateTime.UtcNow,
            amount,
            origin = "Groceries",
            reference = "",
            correlationId = correlationId
         )
   debitWithDate =
      fun amount date ->
         DebitCommand(
            entityId,
            date,
            amount,
            origin = "Groceries",
            reference = "",
            correlationId = correlationId
         )
   depositCash =
      fun amount ->
         DepositCashCommand(
            entityId,
            DateTime.UtcNow,
            amount,
            "",
            correlationId
         )
   limitDailyDebits =
      fun amount -> LimitDailyDebitsCommand(entityId, amount, correlationId)
   registerInternalRecipient =
      RegisterTransferRecipientCommand(
         entityId,
         internalRecipient,
         correlationId
      )
   registerDomesticRecipient =
      RegisterTransferRecipientCommand(
         entityId,
         domesticRecipient,
         correlationId
      )
   domesticTransfer =
      fun amount ->
         TransferCommand(
            entityId,
            correlationId,
            recipient = domesticRecipient,
            date = DateTime.UtcNow,
            amount = amount,
            reference = ""
         )
   internalTransfer =
      fun amount ->
         TransferCommand(
            entityId,
            correlationId,
            recipient = internalRecipient,
            date = DateTime.UtcNow,
            amount = amount,
            reference = ""
         )
   approveTransfer =
      ApproveTransferCommand(
         entityId,
         correlationId,
         recipient = internalRecipient,
         date = DateTime.UtcNow,
         amount = 33m,
         ackReceipt = "123"
      )
   rejectTransfer =
      fun amount ->
         RejectTransferCommand(
            entityId,
            correlationId,
            recipient = internalRecipient,
            date = DateTime.UtcNow,
            amount = amount,
            reason = "-"
         )
   depositTransfer =
      fun amount ->
         DepositTransferCommand(
            entityId,
            amount,
            origin = "Account (9289)",
            correlationId = correlationId
         )
   lockCard = LockCardCommand(entityId, "", correlationId)
   unlockCard = UnlockCardCommand(entityId, "", correlationId)
   maintenanceFee = MaintenanceFeeCommand(entityId)
   skipMaintenanceFee =
      SkipMaintenanceFeeCommand(
         entityId,
         {
            DailyBalanceThreshold = true
            QualifyingDepositFound = false
         }
      )
|}

let accountState = {
   AccountState.empty with
      Status = AccountStatus.Active
      Balance = 300m
}

let accountStateAfterCreate = {
   AccountState.empty with
      EntityId = command.createAccount.EntityId
      Email = Email.deserialize command.createAccount.Email
      FirstName = command.createAccount.FirstName
      LastName = command.createAccount.LastName
      Status = AccountStatus.Active
      Balance = command.createAccount.Balance
      Currency = command.createAccount.Currency
      MaintenanceFeeCriteria = {
         QualifyingDepositFound = false
         DailyBalanceThreshold = true
      }
}

type EventIndex = {
   createdAccount: BankEvent<CreatedAccount>
   depositedCash: BankEvent<DepositedCash>
   debitedAccount: BankEvent<DebitedAccount>
   maintenanceFeeDebited: BankEvent<MaintenanceFeeDebited>
   internalTransferPending: BankEvent<TransferPending>
   domesticTransferPending: BankEvent<TransferPending>
   transferRejected: BankEvent<TransferRejected>
   transferDeposited: BankEvent<TransferDeposited>
}

let event: EventIndex = {
   createdAccount = Result.toValueOption(command.createAccount.toEvent ()).Value
   depositedCash =
      Result.toValueOption(command.depositCash(150m).toEvent ()).Value
   debitedAccount = Result.toValueOption(command.debit(150m).toEvent ()).Value
   maintenanceFeeDebited =
      Result.toValueOption(command.maintenanceFee.toEvent ()).Value
   internalTransferPending =
      Result.toValueOption(command.internalTransfer(20m).toEvent ()).Value
   domesticTransferPending =
      Result.toValueOption(command.domesticTransfer(20m).toEvent ()).Value
   transferRejected =
      Result.toValueOption(command.rejectTransfer(20m).toEvent ()).Value
   transferDeposited =
      Result.toValueOption(command.depositTransfer(100m).toEvent ()).Value
}

let commands: AccountCommand list = [
   AccountCommand.CreateAccount command.createAccount
   AccountCommand.DepositCash
   <| command.depositCash event.depositedCash.Data.DepositedAmount
   AccountCommand.Debit <| command.debit event.debitedAccount.Data.DebitedAmount
   AccountCommand.MaintenanceFee command.maintenanceFee
   AccountCommand.Transfer
   <| command.internalTransfer event.internalTransferPending.Data.DebitedAmount
   AccountCommand.RejectTransfer
   <| command.rejectTransfer event.transferRejected.Data.DebitedAmount
]

let accountEvents = [
   AccountEnvelope.wrap event.createdAccount
   AccountEnvelope.wrap event.depositedCash
   AccountEnvelope.wrap event.debitedAccount
   AccountEnvelope.wrap event.maintenanceFeeDebited
   AccountEnvelope.wrap event.internalTransferPending
   AccountEnvelope.wrap event.transferRejected
]

let billingTransactions: BillingTransaction list = [
   {
      EventId = event.createdAccount.EntityId
      Name = event.createdAccount.EventName
      Amount = event.createdAccount.Data.Balance
      Date = DateTime.UtcNow
      Info = ""
   }
   {
      EventId = event.depositedCash.EntityId
      Name = event.depositedCash.EventName
      Amount = event.depositedCash.Data.DepositedAmount
      Date = DateTime.UtcNow
      Info = ""
   }
   {
      EventId = event.debitedAccount.EntityId
      Name = event.debitedAccount.EventName
      Amount = -event.debitedAccount.Data.DebitedAmount
      Date = DateTime.UtcNow
      Info = ""
   }
   {
      EventId = event.maintenanceFeeDebited.EntityId
      Name = event.maintenanceFeeDebited.EventName
      Amount = -event.maintenanceFeeDebited.Data.DebitedAmount
      Date = DateTime.UtcNow
      Info = ""
   }
   {
      EventId = event.internalTransferPending.EntityId
      Name = event.internalTransferPending.EventName
      Amount = -event.internalTransferPending.Data.DebitedAmount
      Date = DateTime.UtcNow
      Info = ""
   }
   {
      EventId = event.transferRejected.EntityId
      Name = event.transferRejected.EventName
      Amount = event.transferRejected.Data.DebitedAmount
      Date = DateTime.UtcNow
      Info = ""
   }
]

let billingStatement: BillingStatement = {
   Transactions = billingTransactions
   Month = 3
   Year = 2023
   Balance = 250m
   Name = "Jelly Fish"
   AccountId = Guid.NewGuid()
}

let accountBroadcast: SignalRBroadcast = {
   accountEventPersisted = fun (evt, accountState) -> ()
   accountEventValidationFail = fun msg -> ()
   accountEventPersistenceFail = fun msg -> ()
   circuitBreaker = fun msg -> ()
   endBillingCycle = fun () -> ()
}
