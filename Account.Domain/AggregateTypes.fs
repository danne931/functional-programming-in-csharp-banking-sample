namespace Bank.Account.Domain

open System

open Lib.SharedTypes
open Bank.Transfer.Domain
open MaintenanceFee

type AccountCommand =
   | CreateAccount of CreateAccountCommand
   | DepositCash of DepositCashCommand
   | Debit of DebitCommand
   | MaintenanceFee of MaintenanceFeeCommand
   | SkipMaintenanceFee of SkipMaintenanceFeeCommand
   | InternalTransfer of InternalTransferWithinOrgCommand
   | ApproveInternalTransfer of ApproveInternalTransferWithinOrgCommand
   | RejectInternalTransfer of RejectInternalTransferWithinOrgCommand
   | InternalTransferBetweenOrgs of InternalTransferBetweenOrgsCommand
   | ApproveInternalTransferBetweenOrgs of
      ApproveInternalTransferBetweenOrgsCommand
   | RejectInternalTransferBetweenOrgs of
      RejectInternalTransferBetweenOrgsCommand
   | DepositTransferWithinOrg of DepositInternalTransferWithinOrgCommand
   | DepositTransferBetweenOrgs of DepositInternalTransferBetweenOrgsCommand
   | RegisterDomesticTransferRecipient of
      RegisterDomesticTransferRecipientCommand
   | EditDomesticTransferRecipient of EditDomesticTransferRecipientCommand
   | DomesticTransfer of DomesticTransferCommand
   | UpdateDomesticTransferProgress of UpdateDomesticTransferProgressCommand
   | ApproveDomesticTransfer of ApproveDomesticTransferCommand
   | RejectDomesticTransfer of RejectDomesticTransferCommand
   | RequestPlatformPayment of RequestPlatformPaymentCommand
   | CancelPlatformPayment of CancelPlatformPaymentCommand
   | DeclinePlatformPayment of DeclinePlatformPaymentCommand
   | FulfillPlatformPayment of FulfillPlatformPaymentCommand
   | DepositPlatformPayment of DepositPlatformPaymentCommand
   | NicknameRecipient of NicknameRecipientCommand
   | CloseAccount of CloseAccountCommand
   | StartBillingCycle of StartBillingCycleCommand

type AccountEvent =
   | CreatedAccount of BankEvent<CreatedAccount>
   | DepositedCash of BankEvent<DepositedCash>
   | DebitedAccount of BankEvent<DebitedAccount>
   | MaintenanceFeeDebited of BankEvent<MaintenanceFeeDebited>
   | MaintenanceFeeSkipped of BankEvent<MaintenanceFeeSkipped>
   | InternalTransferWithinOrgPending of
      BankEvent<InternalTransferWithinOrgPending>
   | InternalTransferWithinOrgApproved of
      BankEvent<InternalTransferWithinOrgApproved>
   | InternalTransferWithinOrgRejected of
      BankEvent<InternalTransferWithinOrgRejected>
   | InternalTransferBetweenOrgsPending of
      BankEvent<InternalTransferBetweenOrgsPending>
   | InternalTransferBetweenOrgsApproved of
      BankEvent<InternalTransferBetweenOrgsApproved>
   | InternalTransferBetweenOrgsRejected of
      BankEvent<InternalTransferBetweenOrgsRejected>
   | InternalTransferWithinOrgDeposited of
      BankEvent<InternalTransferWithinOrgDeposited>
   | InternalTransferBetweenOrgsDeposited of
      BankEvent<InternalTransferBetweenOrgsDeposited>
   | DomesticTransferRecipient of BankEvent<RegisteredDomesticTransferRecipient>
   | EditedDomesticTransferRecipient of
      BankEvent<EditedDomesticTransferRecipient>
   | DomesticTransferPending of BankEvent<DomesticTransferPending>
   | DomesticTransferProgress of BankEvent<DomesticTransferProgressUpdate>
   | DomesticTransferApproved of BankEvent<DomesticTransferApproved>
   | DomesticTransferRejected of BankEvent<DomesticTransferRejected>
   | PlatformPaymentRequested of BankEvent<PlatformPaymentRequested>
   | PlatformPaymentCancelled of BankEvent<PlatformPaymentCancelled>
   | PlatformPaymentDeclined of BankEvent<PlatformPaymentDeclined>
   | PlatformPaymentPaid of BankEvent<PlatformPaymentPaid>
   | PlatformPaymentDeposited of BankEvent<PlatformPaymentDeposited>
   | RecipientNicknamed of BankEvent<RecipientNicknamed>
   | AccountClosed of BankEvent<AccountClosed>
   | BillingCycleStarted of BankEvent<BillingCycleStarted>

type OpenEventEnvelope = AccountEvent * Envelope

[<RequireQualifiedAccess>]
module AccountEnvelope =
   let get (evt: BankEvent<'E>) : Envelope = {
      Id = evt.Id
      EntityId = evt.EntityId
      OrgId = evt.OrgId
      CorrelationId = evt.CorrelationId
      InitiatedById = evt.InitiatedById
      Timestamp = evt.Timestamp
      EventName = evt.EventName
   }

   let wrap (o: BankEvent<_>) : AccountEvent =
      match box o with
      | :? BankEvent<CreatedAccount> as evt -> CreatedAccount evt
      | :? BankEvent<DepositedCash> as evt -> DepositedCash evt
      | :? BankEvent<DebitedAccount> as evt -> DebitedAccount evt
      | :? BankEvent<MaintenanceFeeDebited> as evt -> MaintenanceFeeDebited evt
      | :? BankEvent<MaintenanceFeeSkipped> as evt -> MaintenanceFeeSkipped evt
      | :? BankEvent<RegisteredDomesticTransferRecipient> as evt ->
         DomesticTransferRecipient evt
      | :? BankEvent<EditedDomesticTransferRecipient> as evt ->
         EditedDomesticTransferRecipient evt
      | :? BankEvent<RecipientNicknamed> as evt ->
         AccountEvent.RecipientNicknamed evt
      | :? BankEvent<InternalTransferWithinOrgPending> as evt ->
         InternalTransferWithinOrgPending evt
      | :? BankEvent<InternalTransferWithinOrgApproved> as evt ->
         InternalTransferWithinOrgApproved evt
      | :? BankEvent<InternalTransferWithinOrgRejected> as evt ->
         InternalTransferWithinOrgRejected evt
      | :? BankEvent<InternalTransferBetweenOrgsPending> as evt ->
         InternalTransferBetweenOrgsPending evt
      | :? BankEvent<InternalTransferBetweenOrgsApproved> as evt ->
         InternalTransferBetweenOrgsApproved evt
      | :? BankEvent<InternalTransferBetweenOrgsRejected> as evt ->
         InternalTransferBetweenOrgsRejected evt
      | :? BankEvent<InternalTransferWithinOrgDeposited> as evt ->
         InternalTransferWithinOrgDeposited evt
      | :? BankEvent<InternalTransferBetweenOrgsDeposited> as evt ->
         InternalTransferBetweenOrgsDeposited evt
      | :? BankEvent<DomesticTransferPending> as evt ->
         DomesticTransferPending evt
      | :? BankEvent<DomesticTransferProgressUpdate> as evt ->
         DomesticTransferProgress evt
      | :? BankEvent<DomesticTransferApproved> as evt ->
         DomesticTransferApproved evt
      | :? BankEvent<DomesticTransferRejected> as evt ->
         DomesticTransferRejected evt
      | :? BankEvent<PlatformPaymentRequested> as evt ->
         PlatformPaymentRequested evt
      | :? BankEvent<PlatformPaymentCancelled> as evt ->
         PlatformPaymentCancelled evt
      | :? BankEvent<PlatformPaymentDeclined> as evt ->
         PlatformPaymentDeclined evt
      | :? BankEvent<PlatformPaymentPaid> as evt -> PlatformPaymentPaid evt
      | :? BankEvent<PlatformPaymentDeposited> as evt ->
         PlatformPaymentDeposited evt
      | :? BankEvent<AccountClosed> as evt -> AccountClosed evt
      | :? BankEvent<BillingCycleStarted> as evt -> BillingCycleStarted evt
      | _ -> failwith "Missing definition for AccountEvent message"

   let unwrap (o: AccountEvent) : OpenEventEnvelope =
      match o with
      | CreatedAccount evt -> wrap evt, get evt
      | DepositedCash evt -> wrap evt, get evt
      | DebitedAccount evt -> wrap evt, get evt
      | MaintenanceFeeDebited evt -> wrap evt, get evt
      | MaintenanceFeeSkipped evt -> wrap evt, get evt
      | InternalTransferWithinOrgPending evt -> wrap evt, get evt
      | InternalTransferWithinOrgApproved evt -> wrap evt, get evt
      | InternalTransferWithinOrgRejected evt -> wrap evt, get evt
      | InternalTransferBetweenOrgsPending evt -> wrap evt, get evt
      | InternalTransferBetweenOrgsApproved evt -> wrap evt, get evt
      | InternalTransferBetweenOrgsRejected evt -> wrap evt, get evt
      | DomesticTransferRecipient evt -> wrap evt, get evt
      | EditedDomesticTransferRecipient evt -> wrap evt, get evt
      | DomesticTransferPending evt -> wrap evt, get evt
      | DomesticTransferProgress evt -> wrap evt, get evt
      | DomesticTransferApproved evt -> wrap evt, get evt
      | DomesticTransferRejected evt -> wrap evt, get evt
      | RecipientNicknamed evt -> wrap evt, get evt
      | InternalTransferWithinOrgDeposited evt -> wrap evt, get evt
      | InternalTransferBetweenOrgsDeposited evt -> wrap evt, get evt
      | PlatformPaymentRequested evt -> wrap evt, get evt
      | PlatformPaymentCancelled evt -> wrap evt, get evt
      | PlatformPaymentDeclined evt -> wrap evt, get evt
      | PlatformPaymentPaid evt -> wrap evt, get evt
      | PlatformPaymentDeposited evt -> wrap evt, get evt
      | AccountClosed evt -> wrap evt, get evt
      | BillingCycleStarted evt -> wrap evt, get evt

type Account = {
   AccountId: AccountId
   OrgId: OrgId
   Name: string
   Depository: AccountDepository
   Currency: Currency
   Status: AccountStatus
   Balance: decimal
   LastBillingCycleDate: DateTime option
   DomesticTransferRecipients: Map<AccountId, DomesticTransferRecipient>
   InProgressInternalTransfers: Map<TransferId, InProgressInternalTransfer>
   InProgressDomesticTransfers: Map<TransferId, DomesticTransfer>
   FailedDomesticTransfers: Map<TransferId, DomesticTransfer>
   MaintenanceFeeCriteria: MaintenanceFeeCriteria
   AccountNumber: AccountNumber
   RoutingNumber: RoutingNumber
} with

   member x.CompositeId = x.AccountId, x.OrgId

type AccountWithEvents = {
   Info: Account
   Events: AccountEvent list
}

type AccountProfile = {
   AccountId: AccountId
   OrgId: OrgId
   Name: string
   Depository: AccountDepository
   AccountNumber: AccountNumber
   RoutingNumber: RoutingNumber
   Balance: decimal
   DailyInternalTransferAccrued: decimal
   DailyDomesticTransferAccrued: decimal
   MonthlyInternalTransferAccrued: decimal
   MonthlyDomesticTransferAccrued: decimal
   DailyPurchaseAccrued: decimal
   MonthlyPurchaseAccrued: decimal
} with

   member x.CompositeId = x.AccountId, x.OrgId

   member x.FullName = $"{x.Name} **{x.AccountNumber.Last4}"

module AccountProfile =
   let fromAccount (account: Account) : AccountProfile = {
      AccountId = account.AccountId
      OrgId = account.OrgId
      Name = account.Name
      Depository = account.Depository
      AccountNumber = account.AccountNumber
      RoutingNumber = account.RoutingNumber
      Balance = account.Balance
      DailyInternalTransferAccrued = 0m
      DailyDomesticTransferAccrued = 0m
      MonthlyInternalTransferAccrued = 0m
      MonthlyDomesticTransferAccrued = 0m
      DailyPurchaseAccrued = 0m
      MonthlyPurchaseAccrued = 0m
   }

type OrgWithAccountProfiles = {
   Org: Org
   AccountProfiles: Map<AccountId, AccountProfile>
   Balance: decimal
}

type AccountMessage =
   | GetAccount
   | StateChange of AccountCommand
   | Event of AccountEvent
   | Delete

type AccountEventPersistedConfirmation = {
   EventPersisted: AccountEvent
   Account: Account
   Date: DateTime
}

type AccountEventRejected = {
   AccountId: AccountId
   Error: Err
   Date: DateTime
}

type AccountBroadcast = {
   accountEventPersisted: AccountEvent -> Account -> unit
   accountEventValidationFail: AccountId -> Err -> unit
   accountEventPersistenceFail: AccountId -> Err -> unit
   circuitBreaker: CircuitBreakerEvent -> unit
}

[<RequireQualifiedAccess>]
type AccountClosureMessage =
   | Register of Account
   | ScheduleDeleteAll
   | DeleteAll of AccountId list
   | GetRegisteredAccounts

[<RequireQualifiedAccess>]
type AccountSeederMessage =
   | SeedAccounts
   | VerifyAccountsCreated

module AccountLoadTestTypes =
   type ProgressCheck = {
      RemainingAccountTests: int
      NumberOfProgressChecks: int
   }

   type LoadTestEventPersisted = {
      AccountId: AccountId
      AccountBalance: decimal
      Event: AccountEvent
   }

   type AccountLoadTestMessage =
      | StartLoadTest
      | CheckProgress of ProgressCheck
      | Finish
      | Teardown
      | Lookup
      | AccountEventPersisted of LoadTestEventPersisted

type TransactionWithAncillaryInfo = {
   Id: EventId
   Event: AccountEvent
   Category: TransactionCategory option
   Note: string option
}
