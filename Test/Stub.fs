[<RequireQualifiedAccess>]
module Stub

open System

open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Transfer.Domain
open BillingStatement

let accountId = Guid.NewGuid() |> AccountId
let orgId = Guid.NewGuid() |> OrgId
let compositeId = accountId, orgId
let correlationId = Guid.NewGuid() |> CorrelationId

let internalRecipient = {
   LastName = "fish"
   FirstName = "blow"
   Nickname = None
   AccountId = Guid.NewGuid() |> AccountId
   Status = RecipientRegistrationStatus.Confirmed
}

let domesticRecipient = {
   LastName = "fish"
   FirstName = "big"
   Nickname = None
   AccountNumber = AccountNumber <| Int64.Parse "123456789123456"
   RoutingNumber = RoutingNumber 123456789
   Status = RecipientRegistrationStatus.Confirmed
   AccountId = Guid.NewGuid() |> AccountId
   Depository = DomesticRecipientAccountDepository.Checking
   PaymentNetwork = PaymentNetwork.ACH
}

let domesticSender: DomesticTransferSender = {
   Name = "small fish"
   AccountNumber = AccountNumber <| Int64.Parse "987654321123456"
   RoutingNumber = RoutingNumber 123456789
   OrgId = orgId
   AccountId = accountId
}

let command = {|
   createAccount =
      CreateAccountCommand.create {
         Email = "smallfish@gmail.com"
         FirstName = "small"
         LastName = "fish"
         Currency = Currency.VND
         AccountId = accountId
         OrgId = orgId
      }
   closeAccount = CloseAccountCommand.create compositeId { Reference = None }
   debit =
      fun amount ->
         DebitCommand.create compositeId {
            Date = DateTime.UtcNow
            Amount = amount
            Origin = "Groceries"
            Reference = None
         }
   debitWithDate =
      fun amount date ->
         DebitCommand.create compositeId {
            Date = date
            Amount = amount
            Origin = "Groceries"
            Reference = None
         }
   depositCash =
      fun amount ->
         DepositCashCommand.create compositeId {
            Amount = amount
            Origin = None
         }
   limitDailyDebits =
      fun amount ->
         LimitDailyDebitsCommand.create compositeId { DebitLimit = amount }
   registerInternalRecipient =
      RegisterInternalTransferRecipientCommand.create compositeId {
         FirstName = internalRecipient.FirstName
         LastName = internalRecipient.LastName
         AccountId = internalRecipient.AccountId
      }
   registerDomesticRecipient =
      RegisterDomesticTransferRecipientCommand.create compositeId {
         FirstName = domesticRecipient.FirstName
         LastName = domesticRecipient.LastName
         AccountNumber = string domesticRecipient.AccountNumber
         RoutingNumber = string domesticRecipient.RoutingNumber
         Depository = DomesticRecipientAccountDepository.Checking
         PaymentNetwork = PaymentNetwork.ACH
      }
   domesticTransfer =
      fun amount ->
         let transferCmd =
            DomesticTransferCommand.create compositeId {
               Sender = domesticSender
               Recipient = domesticRecipient
               TransferRequestDate = DateTime.UtcNow
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
            InternalTransferCommand.create compositeId {
               RecipientId = internalRecipient.AccountId
               Amount = amount
               Reference = None
               TransferRequestDate = DateTime.UtcNow
            }

         {
            transferCmd with
               CorrelationId = correlationId
         }
   approveInternalTransfer =
      ApproveInternalTransferCommand.create compositeId correlationId {
         RecipientId = internalRecipient.AccountId
         Amount = 33m
         TransferRequestDate = DateTime.UtcNow
      }
   rejectInternalTransfer =
      fun amount ->
         RejectInternalTransferCommand.create compositeId correlationId {
            RecipientId = internalRecipient.AccountId
            Amount = amount
            Reason = TransferDeclinedReason.AccountClosed
            TransferRequestDate = DateTime.UtcNow
         }
   depositTransfer =
      fun amount ->
         DepositTransferCommand.create compositeId correlationId {
            Amount = amount
            Origin = Guid.NewGuid() |> AccountId
         }
   lockCard = LockCardCommand.create compositeId { Reference = None }
   unlockCard = UnlockCardCommand.create compositeId { Reference = None }
   maintenanceFee = MaintenanceFeeCommand.create compositeId
   skipMaintenanceFee =
      SkipMaintenanceFeeCommand.create compositeId {
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
   internalTransferPending: BankEvent<InternalTransferPending>
   domesticTransferPending: BankEvent<DomesticTransferPending>
   internalTransferRejected: BankEvent<InternalTransferRejected>
   transferDeposited: BankEvent<TransferDeposited>
}

let event: EventIndex = {
   createdAccount =
      command.createAccount
      |> CreateAccountCommand.toEvent
      |> Result.toValueOption
      |> _.Value
   depositedCash =
      command.depositCash 2000m
      |> DepositCashCommand.toEvent
      |> Result.toValueOption
      |> _.Value
   debitedAccount =
      command.debit 150m
      |> DebitCommand.toEvent
      |> Result.toValueOption
      |> _.Value
   maintenanceFeeDebited =
      command.maintenanceFee
      |> MaintenanceFeeCommand.toEvent
      |> Result.toValueOption
      |> _.Value
   internalTransferPending =
      command.internalTransfer 20m
      |> InternalTransferCommand.toEvent
      |> Result.toValueOption
      |> _.Value
   domesticTransferPending =
      command.domesticTransfer 20m
      |> DomesticTransferCommand.toEvent
      |> Result.toValueOption
      |> _.Value
   internalTransferRejected =
      command.rejectInternalTransfer 20m
      |> RejectInternalTransferCommand.toEvent
      |> Result.toValueOption
      |> _.Value
   transferDeposited =
      command.depositTransfer 100m
      |> DepositTransferCommand.toEvent
      |> Result.toValueOption
      |> _.Value
}

let commands: AccountCommand list = [
   AccountCommand.CreateAccount command.createAccount

   AccountCommand.DepositCash
   <| command.depositCash event.depositedCash.Data.Amount

   AccountCommand.Debit <| command.debit event.debitedAccount.Data.Amount

   AccountCommand.MaintenanceFee command.maintenanceFee

   AccountCommand.RegisterInternalTransferRecipient
      command.registerInternalRecipient

   AccountCommand.InternalTransfer
   <| command.internalTransfer event.internalTransferPending.Data.Amount

   AccountCommand.RejectInternalTransfer
   <| command.rejectInternalTransfer event.internalTransferRejected.Data.Amount
]

let accountEvents = [
   AccountEnvelope.wrap event.createdAccount
   AccountEnvelope.wrap event.depositedCash
   AccountEnvelope.wrap event.debitedAccount
   AccountEnvelope.wrap event.maintenanceFeeDebited
   AccountEnvelope.wrap event.internalTransferPending
   AccountEnvelope.wrap event.internalTransferRejected
]

let accountState = {
   Account.empty with
      Status = AccountStatus.Active
      Balance = 300m
}

let accountStateAfterCreate = {
   Account.empty with
      AccountId = command.createAccount.Data.AccountId
      OrgId = command.createAccount.OrgId
      Email = Email.deserialize command.createAccount.Data.Email
      FirstName = command.createAccount.Data.FirstName
      LastName = command.createAccount.Data.LastName
      Status = AccountStatus.Active
      Balance = 0m
      Currency = command.createAccount.Data.Currency
      MaintenanceFeeCriteria = {
         QualifyingDepositFound = false
         DailyBalanceThreshold = false
      }
      Events = [ AccountEnvelope.wrap event.createdAccount ]
}

let accountStateOmitEvents (accountOpt: Account option) =
   accountOpt
   |> Option.map (fun account -> {|
      EntityId = account.AccountId
      Email = account.Email
      FirstName = account.FirstName
      LastName = account.LastName
      Currency = account.Currency
      Status = account.Status
      Balance = account.Balance
      DailyDebitLimit = account.DailyDebitLimit
      DailyDebitAccrued = account.DailyDebitAccrued
      InternalTransferRecipients = account.InternalTransferRecipients
      DomesticTransferRecipients = account.DomesticTransferRecipients
      MaintenanceFeeCriteria = account.MaintenanceFeeCriteria
   |})

let billingTransactions: BillingTransaction list = [
   event.createdAccount
   |> AccountEvent.CreatedAccount
   |> BillingTransaction.create
   |> _.Value

   event.depositedCash
   |> AccountEvent.DepositedCash
   |> BillingTransaction.create
   |> _.Value

   event.debitedAccount
   |> AccountEvent.DebitedAccount
   |> BillingTransaction.create
   |> _.Value

   event.maintenanceFeeDebited
   |> AccountEvent.MaintenanceFeeDebited
   |> BillingTransaction.create
   |> _.Value

   event.internalTransferPending
   |> AccountEvent.InternalTransferPending
   |> BillingTransaction.create
   |> _.Value

   event.internalTransferRejected
   |> AccountEvent.InternalTransferRejected
   |> BillingTransaction.create
   |> _.Value
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
