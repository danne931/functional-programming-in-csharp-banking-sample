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
   | LimitDailyDebits of LimitDailyDebitsCommand
   | LockCard of LockCardCommand
   | UnlockCard of UnlockCardCommand
   | Transfer of TransferCommand
   | UpdateTransferProgress of UpdateTransferProgressCommand
   | ApproveTransfer of ApproveTransferCommand
   | RejectTransfer of RejectTransferCommand
   | DepositTransfer of DepositTransferCommand
   | RegisterTransferRecipient of RegisterTransferRecipientCommand
   | RegisterInternalSender of RegisterInternalSenderCommand
   | DeactivateInternalRecipient of DeactivateInternalRecipientCommand
   | NicknameRecipient of NicknameRecipientCommand
   | CloseAccount of CloseAccountCommand
   | StartBillingCycle of StartBillingCycleCommand

type AccountEvent =
   | CreatedAccount of BankEvent<CreatedAccount>
   | DepositedCash of BankEvent<DepositedCash>
   | DebitedAccount of BankEvent<DebitedAccount>
   | MaintenanceFeeDebited of BankEvent<MaintenanceFeeDebited>
   | MaintenanceFeeSkipped of BankEvent<MaintenanceFeeSkipped>
   | DailyDebitLimitUpdated of BankEvent<DailyDebitLimitUpdated>
   | LockedCard of BankEvent<LockedCard>
   | UnlockedCard of BankEvent<UnlockedCard>
   | TransferPending of BankEvent<TransferPending>
   | TransferProgress of BankEvent<TransferProgressUpdate>
   | TransferApproved of BankEvent<TransferApproved>
   | TransferRejected of BankEvent<TransferRejected>
   | TransferDeposited of BankEvent<TransferDeposited>
   | InternalTransferRecipient of BankEvent<RegisteredInternalTransferRecipient>
   | DomesticTransferRecipient of BankEvent<RegisteredDomesticTransferRecipient>
   | InternalRecipientDeactivated of BankEvent<InternalRecipientDeactivated>
   | RecipientNicknamed of BankEvent<RecipientNicknamed>
   | InternalSenderRegistered of BankEvent<InternalSenderRegistered>
   | AccountClosed of BankEvent<AccountClosed>
   | BillingCycleStarted of BankEvent<BillingCycleStarted>

type OpenEventEnvelope = AccountEvent * Envelope

[<RequireQualifiedAccess>]
module AccountEnvelope =
   let private get (evt: BankEvent<'E>) = {
      Id = evt.Id
      EntityId = evt.EntityId
      CorrelationId = evt.CorrelationId
      Timestamp = evt.Timestamp
      EventName = evt.EventName
   }

   let wrap (o: BankEvent<_>) : AccountEvent =
      match box o with
      | :? BankEvent<CreatedAccount> as evt -> evt |> CreatedAccount
      | :? BankEvent<DepositedCash> as evt -> evt |> DepositedCash
      | :? BankEvent<DebitedAccount> as evt -> evt |> DebitedAccount
      | :? BankEvent<MaintenanceFeeDebited> as evt ->
         evt |> MaintenanceFeeDebited
      | :? BankEvent<MaintenanceFeeSkipped> as evt ->
         evt |> MaintenanceFeeSkipped
      | :? BankEvent<DailyDebitLimitUpdated> as evt ->
         evt |> DailyDebitLimitUpdated
      | :? BankEvent<LockedCard> as evt -> evt |> LockedCard
      | :? BankEvent<UnlockedCard> as evt -> evt |> UnlockedCard
      | :? BankEvent<RegisteredInternalTransferRecipient> as evt ->
         evt |> InternalTransferRecipient
      | :? BankEvent<RegisteredDomesticTransferRecipient> as evt ->
         evt |> DomesticTransferRecipient
      | :? BankEvent<InternalRecipientDeactivated> as evt ->
         evt |> InternalRecipientDeactivated
      | :? BankEvent<RecipientNicknamed> as evt ->
         evt |> AccountEvent.RecipientNicknamed
      | :? BankEvent<InternalSenderRegistered> as evt ->
         evt |> InternalSenderRegistered
      | :? BankEvent<TransferPending> as evt -> evt |> TransferPending
      | :? BankEvent<TransferProgressUpdate> as evt -> evt |> TransferProgress
      | :? BankEvent<TransferApproved> as evt -> evt |> TransferApproved
      | :? BankEvent<TransferRejected> as evt -> evt |> TransferRejected
      | :? BankEvent<TransferDeposited> as evt -> evt |> TransferDeposited
      | :? BankEvent<AccountClosed> as evt -> evt |> AccountClosed
      | :? BankEvent<BillingCycleStarted> as evt -> evt |> BillingCycleStarted
      | _ -> failwith "Missing definition for AccountEvent message"

   let unwrap (o: AccountEvent) : OpenEventEnvelope =
      match o with
      | CreatedAccount evt -> wrap evt, get evt
      | DepositedCash evt -> wrap evt, get evt
      | DebitedAccount evt -> wrap evt, get evt
      | MaintenanceFeeDebited evt -> wrap evt, get evt
      | MaintenanceFeeSkipped evt -> wrap evt, get evt
      | DailyDebitLimitUpdated evt -> wrap evt, get evt
      | LockedCard evt -> wrap evt, get evt
      | UnlockedCard evt -> wrap evt, get evt
      | InternalTransferRecipient evt -> wrap evt, get evt
      | DomesticTransferRecipient evt -> wrap evt, get evt
      | InternalRecipientDeactivated evt -> wrap evt, get evt
      | RecipientNicknamed evt -> wrap evt, get evt
      | InternalSenderRegistered evt -> wrap evt, get evt
      | TransferPending evt -> wrap evt, get evt
      | TransferProgress evt -> wrap evt, get evt
      | TransferApproved evt -> wrap evt, get evt
      | TransferRejected evt -> wrap evt, get evt
      | TransferDeposited evt -> wrap evt, get evt
      | AccountClosed evt -> wrap evt, get evt
      | BillingCycleStarted evt -> wrap evt, get evt

[<RequireQualifiedAccess>]
type AccountStatus =
   | Pending
   | Active
   | Closed
   | ReadyForDelete

type Account = {
   EntityId: Guid
   Email: Email
   FirstName: string
   LastName: string
   Currency: Currency
   Status: AccountStatus
   Balance: decimal
   DailyDebitLimit: decimal
   DailyDebitAccrued: decimal
   DailyInternalTransferAccrued: decimal
   DailyDomesticTransferAccrued: decimal
   LastDebitDate: DateTime option
   LastInternalTransferDate: DateTime option
   LastDomesticTransferDate: DateTime option
   LastBillingCycleDate: DateTime option
   InternalTransferSenders: Map<Guid, InternalTransferSender>
   TransferRecipients: Map<string, TransferRecipient>
   InProgressTransfers: Map<string, TransferTransaction>
   MaintenanceFeeCriteria: MaintenanceFeeCriteria
   Events: AccountEvent list
   CardLocked: bool
} with

   member x.Name = $"{x.FirstName} {x.LastName}"

type AccountProfile = {
   EntityId: Guid
   Email: Email
   FirstName: string
   LastName: string
} with

   member x.Name = $"{x.FirstName} {x.LastName}"

type AccountMessage =
   | UserCreationResponse of Result<int, Err> * BankEvent<CreatedAccount>
   | GetAccount
   | GetEvents
   | StateChange of AccountCommand
   | Event of AccountEvent
   | Delete

type AccountEventPersistedConfirmation = {
   EventPersisted: AccountEvent
   NewState: Account
   Date: DateTime
}

type AccountEventRejected = {
   AccountId: Guid
   Error: Err
   Date: DateTime
}

[<RequireQualifiedAccess>]
type SignalRMessage =
   | AccountEventPersisted of AccountEventPersistedConfirmation
   | AccountEventValidationFail of AccountEventRejected
   | AccountEventPersistenceFail of AccountEventRejected
   | CircuitBreaker of CircuitBreakerEvent

type AccountPersistence = {
   getEvents: Guid -> AccountEvent list Async
}

type AccountBroadcast = {
   accountEventPersisted: AccountEvent -> Account -> unit
   accountEventValidationFail: Guid -> Err -> unit
   accountEventPersistenceFail: Guid -> Err -> unit
   circuitBreaker: CircuitBreakerEvent -> unit
}

[<RequireQualifiedAccess>]
type AccountClosureMessage =
   | Register of Account
   | ScheduleDeleteAll
   | DeleteAll of Guid list
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
      AccountId: Guid
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

type TransactionCategory = { Id: int; Name: string }

type TransactionWithAncillaryInfo = {
   Id: Guid
   Event: AccountEvent
   Category: TransactionCategory option
   Note: string option
}
