namespace Bank.Account.Domain

open System

open Lib.Types
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
   | AccountClosed of BankEvent<AccountClosed>
   | BillingCycleStarted of BankEvent<BillingCycleStarted>

type OpenEventEnvelope = AccountEvent * Envelope

[<RequireQualifiedAccess>]
module AccountEnvelope =
   let private get (evt: BankEvent<'E>) = {
      EntityId = evt.EntityId
      Timestamp = evt.Timestamp
      EventName = evt.EventName
      CorrelationId = evt.CorrelationId
   }

   let wrap (o: obj) : AccountEvent =
      match o with
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
      | :? BankEvent<TransferPending> as evt -> evt |> TransferPending
      | :? BankEvent<TransferProgressUpdate> as evt -> evt |> TransferProgress
      | :? BankEvent<TransferApproved> as evt -> evt |> TransferApproved
      | :? BankEvent<TransferRejected> as evt -> evt |> TransferRejected
      | :? BankEvent<TransferDeposited> as evt -> evt |> TransferDeposited
      | :? BankEvent<AccountClosed> as evt -> evt |> AccountClosed
      | :? BankEvent<BillingCycleStarted> as evt -> evt |> BillingCycleStarted

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
      | TransferPending evt -> wrap evt, get evt
      | TransferProgress evt -> wrap evt, get evt
      | TransferApproved evt -> wrap evt, get evt
      | TransferRejected evt -> wrap evt, get evt
      | TransferDeposited evt -> wrap evt, get evt
      | AccountClosed evt -> wrap evt, get evt
      | BillingCycleStarted evt -> wrap evt, get evt

type AccountStatus =
   | Pending
   | Active
   | Closed
   | ReadyForDelete

type AccountState = {
   EntityId: Guid
   Email: Email
   FirstName: string
   LastName: string
   Currency: Currency
   Status: AccountStatus
   Balance: decimal
   DailyDebitLimit: decimal
   DailyDebitAccrued: decimal
   LastDebitDate: DateTime option
   LastBillingCycleDate: DateTime option
   TransferRecipients: Map<string, TransferRecipient>
   InProgressTransfers: Map<string, TransferTransaction>
   MaintenanceFeeCriteria: MaintenanceFeeCriteria
   Events: AccountEvent list
   CardLocked: bool
} with

   static member empty = {
      EntityId = Guid.Empty
      Email = Email.empty
      FirstName = ""
      LastName = ""
      Currency = Currency.USD
      Status = AccountStatus.Pending
      Balance = 0m
      DailyDebitLimit = -1m
      DailyDebitAccrued = 0m
      LastDebitDate = None
      LastBillingCycleDate = None
      TransferRecipients = Map.empty
      InProgressTransfers = Map.empty
      MaintenanceFeeCriteria = {
         QualifyingDepositFound = false
         DailyBalanceThreshold = false
      }
      Events = []
      CardLocked = false
   }

   member x.Name = $"{x.FirstName} {x.LastName}"

type AccountMessage =
   | UserCreationResponse of Result<int, Err> * BankEvent<CreatedAccount>
   | GetAccount
   | GetEvents
   | StateChange of AccountCommand
   | Event of AccountEvent
   | Delete

type CircuitBreakerService =
   | DomesticTransfer
   | Email

type CircuitBreakerStatus =
   | Closed
   | HalfOpen
   | Open

type CircuitBreakerEvent = {
   Service: CircuitBreakerService
   Status: CircuitBreakerStatus
   Timestamp: DateTime
}

type CircuitBreakerMessage =
   | Lookup
   | CircuitBreaker of CircuitBreakerEvent

type CircuitBreakerActorState = {
   DomesticTransfer: CircuitBreakerStatus
   Email: CircuitBreakerStatus
}

type SignalRMessage =
   | AccountEventPersisted of AccountEvent * AccountState
   | AccountEventValidationFail of Guid * string
   | AccountEventPersistenceFail of Guid * string
   | CircuitBreaker of CircuitBreakerEvent
   | EndBillingCycle

type AccountPersistence = {
   getEvents: Guid -> AccountEvent list Async
}

type AccountBroadcast = {
   accountEventPersisted: AccountEvent -> AccountState -> unit
   accountEventValidationFail: Guid -> string -> unit
   accountEventPersistenceFail: Guid -> string -> unit
   circuitBreaker: CircuitBreakerEvent -> unit
}

type AccountClosureMessage =
   | Register of AccountState
   | ScheduleDeleteAll
   | DeleteAll of Guid list
   | ReverseClosure of Guid
   | GetRegisteredAccounts

type AccountSeederMessage =
   | SeedAccounts
   | VerifyAccountsCreated
   | VerifiedAccountsReceived of AccountState list
   | ErrorVerifyingAccounts of Err

type AccountEventConsumerState = {
   Offset: Akka.Persistence.Query.Sequence
}

type AccountEventConsumerMessage = SaveOffset of Akka.Persistence.Query.Sequence

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
