[<RequireQualifiedAccess>]
module Stub

open System
open System.Threading.Tasks

open BankTypes
open Lib.Types
open Bank.Account.Domain
open Bank.Transfer.Domain

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

let event = {|
   createdAccount =
      CreatedAccount {
         EntityId = entityId
         CorrelationId = correlationId
         Timestamp = DateTime.UtcNow
         Data = {
            Email = Email.deserialize "jellyfish@gmail.com"
            FirstName = "Jelly"
            LastName = "Fish"
            Balance = 100m
            Currency = Currency.THB
         }
      }
   depositedCash =
      DepositedCash {
         EntityId = entityId
         CorrelationId = correlationId
         Timestamp = DateTime.UtcNow
         Data = {
            DepositedAmount = 150m
            Origin = "ATM"
         }
      }
   internalTransferPending = {
      EntityId = entityId
      CorrelationId = correlationId
      Timestamp = DateTime.UtcNow
      Data = {
         Date = DateTime.UtcNow
         Reference = None
         DebitedAmount = 20m
         Recipient = {
            LastName = "Fish"
            FirstName = "Small"
            Identification = Guid.NewGuid().ToString()
            AccountEnvironment = RecipientAccountEnvironment.Internal
            IdentificationStrategy =
               RecipientAccountIdentificationStrategy.AccountId
            RoutingNumber = None
            Currency = USD
         }
      }
   }
   domesticTransferPending = {
      EntityId = entityId
      CorrelationId = correlationId
      Timestamp = DateTime.UtcNow
      Data = {
         Date = DateTime.UtcNow
         Reference = None
         DebitedAmount = 20m
         Recipient = {
            LastName = "Fish"
            FirstName = "Big"
            Identification = Guid.NewGuid().ToString()
            AccountEnvironment = RecipientAccountEnvironment.Domestic
            IdentificationStrategy =
               RecipientAccountIdentificationStrategy.AccountId
            RoutingNumber = Some "123459991"
            Currency = USD
         }
      }
   }
|}

let transactions = [ event.createdAccount; event.depositedCash ]

let billingStatement: BillingStatement.BillingStatement = {
   Transactions = transactions
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
