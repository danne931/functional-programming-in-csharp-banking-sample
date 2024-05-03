[<RequireQualifiedAccess>]
module Stub

open System

open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Transfer.Domain
open BillingStatement

let entityId = Guid.NewGuid()
let correlationId = Guid.NewGuid()

let internalRecipient = {
   LastName = "fish"
   FirstName = "blow"
   Identification = Guid.NewGuid().ToString()
   IdentificationStrategy = RecipientAccountIdentificationStrategy.AccountId
   AccountEnvironment = RecipientAccountEnvironment.Internal
   RoutingNumber = None
   Status = RecipientRegistrationStatus.Confirmed
}

let domesticRecipient = {
   LastName = "fish"
   FirstName = "big"
   Identification = Guid.NewGuid().ToString()
   IdentificationStrategy = RecipientAccountIdentificationStrategy.AccountId
   AccountEnvironment = RecipientAccountEnvironment.Domestic
   RoutingNumber = Some "1992384"
   Status = RecipientRegistrationStatus.Confirmed
}

let command = {|
   createAccount =
      CreateAccountCommand.create entityId {
         Email = "smallfish@gmail.com"
         Balance = 2000m
         FirstName = "small"
         LastName = "fish"
         Currency = Currency.VND
      }
   closeAccount = CloseAccountCommand.create entityId { Reference = None }
   debit =
      fun amount ->
         DebitCommand.create entityId {
            Date = DateTime.UtcNow
            Amount = amount
            Origin = "Groceries"
            Reference = None
         }
   debitWithDate =
      fun amount date ->
         DebitCommand.create entityId {
            Date = date
            Amount = amount
            Origin = "Groceries"
            Reference = None
         }
   depositCash =
      fun amount ->
         DepositCashCommand.create entityId {
            Date = DateTime.UtcNow
            Amount = amount
            Origin = None
         }
   limitDailyDebits =
      fun amount ->
         LimitDailyDebitsCommand.create entityId { DebitLimit = amount }
   registerInternalRecipient =
      RegisterTransferRecipientCommand.create entityId {
         Recipient = internalRecipient
      }
   registerDomesticRecipient =
      RegisterTransferRecipientCommand.create entityId {
         Recipient = domesticRecipient
      }
   domesticTransfer =
      fun amount ->
         let transferCmd =
            TransferCommand.create entityId {
               Recipient = domesticRecipient
               Date = DateTime.UtcNow
               Amount = amount
               Reference = None
            }

         {
            transferCmd with
               CorrelationId = correlationId
         }
   internalTransfer =
      fun amount ->
         let transferCmd =
            TransferCommand.create entityId {
               Recipient = internalRecipient
               Date = DateTime.UtcNow
               Amount = amount
               Reference = None
            }

         {
            transferCmd with
               CorrelationId = correlationId
         }
   approveTransfer =
      ApproveTransferCommand.create entityId correlationId {
         Recipient = internalRecipient
         Date = DateTime.UtcNow
         Amount = 33m
      }
   rejectTransfer =
      fun amount ->
         RejectTransferCommand.create entityId correlationId {
            Recipient = internalRecipient
            Date = DateTime.UtcNow
            Amount = amount
            Reason = TransferDeclinedReason.AccountClosed
         }
   depositTransfer =
      fun amount ->
         DepositTransferCommand.create entityId correlationId {
            Amount = amount
            Origin = "Account (9289)"
         }
   lockCard = LockCardCommand.create entityId { Reference = None }
   unlockCard = UnlockCardCommand.create entityId { Reference = None }
   maintenanceFee = MaintenanceFeeCommand.create entityId
   skipMaintenanceFee =
      SkipMaintenanceFeeCommand.create entityId {
         Reason = {
            DailyBalanceThreshold = true
            QualifyingDepositFound = false
         }
      }
|}

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
      Result
         .toValueOption(CreateAccountCommand.toEvent command.createAccount)
         .Value
   depositedCash =
      Result
         .toValueOption(
            DepositCashCommand.toEvent <| command.depositCash (150m)
         )
         .Value
   debitedAccount =
      Result.toValueOption(DebitCommand.toEvent <| command.debit (150m)).Value
   maintenanceFeeDebited =
      Result
         .toValueOption(MaintenanceFeeCommand.toEvent <| command.maintenanceFee)
         .Value
   internalTransferPending =
      Result
         .toValueOption(
            TransferCommand.toEvent <| command.internalTransfer (20m)
         )
         .Value
   domesticTransferPending =
      Result
         .toValueOption(
            TransferCommand.toEvent <| command.domesticTransfer (20m)
         )
         .Value
   transferRejected =
      Result
         .toValueOption(
            RejectTransferCommand.toEvent <| command.rejectTransfer (20m)
         )
         .Value
   transferDeposited =
      Result
         .toValueOption(
            DepositTransferCommand.toEvent <| command.depositTransfer (100m)
         )
         .Value
}

let commands: AccountCommand list = [
   AccountCommand.CreateAccount command.createAccount
   AccountCommand.DepositCash
   <| command.depositCash event.depositedCash.Data.DepositedAmount
   AccountCommand.Debit <| command.debit event.debitedAccount.Data.DebitedAmount
   AccountCommand.MaintenanceFee command.maintenanceFee
   AccountCommand.RegisterTransferRecipient command.registerInternalRecipient
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

let accountState = {
   Account.empty with
      Status = AccountStatus.Active
      Balance = 300m
}

let accountStateAfterCreate = {
   Account.empty with
      EntityId = command.createAccount.EntityId
      Email = Email.deserialize command.createAccount.Data.Email
      FirstName = command.createAccount.Data.FirstName
      LastName = command.createAccount.Data.LastName
      Status = AccountStatus.Active
      Balance = command.createAccount.Data.Balance
      Currency = command.createAccount.Data.Currency
      MaintenanceFeeCriteria = {
         QualifyingDepositFound = false
         DailyBalanceThreshold = true
      }
      Events = [ AccountEnvelope.wrap event.createdAccount ]
}

let accountStateOmitEvents (accountOpt: Account option) =
   accountOpt
   |> Option.map (fun account -> {|
      EntityId = account.EntityId
      Email = account.Email
      FirstName = account.FirstName
      LastName = account.LastName
      Currency = account.Currency
      Status = account.Status
      Balance = account.Balance
      DailyDebitLimit = account.DailyDebitLimit
      DailyDebitAccrued = account.DailyDebitAccrued
      TransferRecipients = account.TransferRecipients
      MaintenanceFeeCriteria = account.MaintenanceFeeCriteria
   |})

let billingTransactions: BillingTransaction list = [
   (event.createdAccount
    |> AccountEvent.CreatedAccount
    |> BillingTransaction.create)
      .Value
   (event.depositedCash
    |> AccountEvent.DepositedCash
    |> BillingTransaction.create)
      .Value
   (event.debitedAccount
    |> AccountEvent.DebitedAccount
    |> BillingTransaction.create)
      .Value
   (event.maintenanceFeeDebited
    |> AccountEvent.MaintenanceFeeDebited
    |> BillingTransaction.create)
      .Value
   (event.internalTransferPending
    |> AccountEvent.TransferPending
    |> BillingTransaction.create)
      .Value
   (event.transferRejected
    |> AccountEvent.TransferRejected
    |> BillingTransaction.create)
      .Value
]

let billingStatement =
   BillingStatement.billingStatement accountStateAfterCreate Int64.MaxValue

let accountBroadcast: AccountBroadcast = {
   accountEventPersisted = fun evt accountState -> ()
   accountEventValidationFail = fun accountId msg -> ()
   accountEventPersistenceFail = fun accountId msg -> ()
   circuitBreaker = fun msg -> ()
}

let akkaStreamsRestartSettings () =
   Akka.Streams.RestartSettings.Create(
      (TimeSpan.FromSeconds 3),
      (TimeSpan.FromSeconds 30),
      0.2
   )
