[<RequireQualifiedAccess>]
module Stub

open System
open System.Threading.Tasks

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

let internalTransferRecipient = {
   LastName = "Fish"
   FirstName = "Small"
   Identification = Guid.NewGuid().ToString()
   AccountEnvironment = RecipientAccountEnvironment.Internal
   IdentificationStrategy = RecipientAccountIdentificationStrategy.AccountId
   RoutingNumber = None
   Currency = USD
}

let domesticTransferRecipient = {
   LastName = "Fish"
   FirstName = "Big"
   Identification = Guid.NewGuid().ToString()
   AccountEnvironment = RecipientAccountEnvironment.Domestic
   IdentificationStrategy = RecipientAccountIdentificationStrategy.AccountId
   RoutingNumber = Some "123459991"
   Currency = USD
}

let asBankEvent (evtData: 't) : BankEvent<'t> = {
   EntityId = entityId
   CorrelationId = correlationId
   Timestamp = DateTime.UtcNow
   Data = evtData
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
   createdAccount =
      asBankEvent {
         Email = Email.deserialize "jellyfish@gmail.com"
         FirstName = "Jelly"
         LastName = "Fish"
         Balance = 100m
         Currency = Currency.THB
      }
   depositedCash =
      asBankEvent {
         DepositedAmount = 150m
         Origin = "ATM"
      }
   debitedAccount =
      asBankEvent {
         DebitedAmount = 150m
         Origin = "Spotify"
         Date = DateTime.UtcNow
         Reference = None
      }
   maintenanceFeeDebited =
      asBankEvent {
         DebitedAmount = MaintenanceFee.RecurringDebitAmount
      }
   internalTransferPending =
      asBankEvent {
         Date = DateTime.UtcNow
         Reference = None
         DebitedAmount = 20m
         Recipient = internalTransferRecipient
      }
   domesticTransferPending =
      asBankEvent {
         Date = DateTime.UtcNow
         Reference = None
         DebitedAmount = 20m
         Recipient = domesticTransferRecipient
      }
   transferRejected =
      asBankEvent {
         Date = DateTime.UtcNow
         DebitedAmount = 20m
         Reason = ""
         Recipient = domesticTransferRecipient
      }
   transferDeposited = asBankEvent ({ DepositedAmount = 100m; Origin = "" })
}

let accountEvents = [
   AccountEnvelope.wrap event.createdAccount
   AccountEnvelope.wrap event.depositedCash
   AccountEnvelope.wrap event.debitedAccount
   AccountEnvelope.wrap event.maintenanceFeeDebited
   AccountEnvelope.wrap event.internalTransferPending
   AccountEnvelope.wrap event.transferRejected
   AccountEnvelope.wrap event.transferDeposited
]

let billingTransactions: BillingTransaction list = [
   {
      EventId = Guid.NewGuid()
      Name = "CreatedAccount"
      Amount = 100m
      Date = DateTime.UtcNow
      Info = ""
   }
   {
      EventId = Guid.NewGuid()
      Name = "DepositedCash"
      Amount = 150m
      Date = DateTime.UtcNow
      Info = ""
   }
   {
      EventId = Guid.NewGuid()
      Name = "DebitedAccount"
      Amount = -150m
      Date = DateTime.UtcNow
      Info = ""
   }
   {
      EventId = Guid.NewGuid()
      Name = "MaintenanceFeeDebited"
      Amount = -MaintenanceFee.RecurringDebitAmount
      Date = DateTime.UtcNow
      Info = ""
   }
   {
      EventId = Guid.NewGuid()
      Name = "TransferPending"
      Amount = -20m
      Date = DateTime.UtcNow
      Info = ""
   }
   {
      EventId = Guid.NewGuid()
      Name = "TransferRejected"
      Amount = 20m
      Date = DateTime.UtcNow
      Info = ""
   }
   {
      EventId = Guid.NewGuid()
      Name = "TransferDeposited"
      Amount = 100m
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

let accountBroadcast: AccountBroadcast = {
   broadcast = fun (evt, accountState) -> Task.FromResult()
   broadcastError = fun msg -> Task.FromResult()
   broadcastCircuitBreaker = fun msg -> Task.FromResult()
   broadcastBillingCycleEnd = fun () -> Task.FromResult()
}
