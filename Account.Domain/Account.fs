[<RequireQualifiedAccess>]
module Account

open Validus

open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.SharedTypes
open Lib.Time

let empty = {
   EntityId = System.Guid.Empty
   Email = Email.empty
   FirstName = ""
   LastName = ""
   Currency = Currency.USD
   Status = AccountStatus.Pending
   Balance = 0m
   DailyDebitLimit = 2000m
   DailyDebitAccrued = 0m
   DailyInternalTransferAccrued = 0m
   DailyDomesticTransferAccrued = 0m
   LastDebitDate = None
   LastInternalTransferDate = None
   LastDomesticTransferDate = None
   LastBillingCycleDate = None
   TransferRecipients = Map.empty
   InProgressTransfers = Map.empty
   InternalTransferSenders = Map.empty
   MaintenanceFeeCriteria = {
      QualifyingDepositFound = false
      DailyBalanceThreshold = false
   }
   Events = []
   CardLocked = false
}

let recipientLookupKey (recipient: TransferRecipient) =
   match recipient.RoutingNumber with
   | None -> recipient.Identification
   | Some routingNum -> $"{routingNum}_{recipient.Identification}"

let dailyDebitAccrued state (evt: BankEvent<DebitedAccount>) : decimal =
   // When applying a new event to the cached Account & the
   // last debit event did not occur today...
   // -> Ignore the cached DailyDebitAccrued
   let accrued =
      if state.LastDebitDate.IsSome && IsToday state.LastDebitDate.Value then
         state.DailyDebitAccrued
      else
         0m

   // When accumulating events into Account aggregate...
   // -> Ignore debits older than a day
   if IsToday evt.Data.Date then
      accrued + evt.Data.DebitedAmount
   else
      accrued

module TransferLimits =
   let DailyInternalLimit = 999_999_999m
   let DailyDomesticLimit = 100_000m

   let exceedsDailyTransferLimit
      (account: Account)
      (cmd: TransferCommand)
      : bool
      =
      let o = cmd.Data

      match o.Recipient.AccountEnvironment with
      | RecipientAccountEnvironment.Internal ->
         IsToday o.Date
         && account.DailyInternalTransferAccrued + o.Amount > DailyInternalLimit
      | RecipientAccountEnvironment.Domestic ->
         IsToday o.Date
         && account.DailyDomesticTransferAccrued + o.Amount > DailyDomesticLimit

   let accruedDailyTransfers
      (account: Account)
      (evt: BankEvent<TransferPending>)
      =
      let lastTransferDate, accrued =
         match evt.Data.Recipient.AccountEnvironment with
         | RecipientAccountEnvironment.Internal ->
            account.LastInternalTransferDate,
            account.DailyInternalTransferAccrued
         | RecipientAccountEnvironment.Domestic ->
            account.LastDomesticTransferDate,
            account.DailyDomesticTransferAccrued

      let accrued =
         match lastTransferDate with
         | Some date when IsToday date -> accrued
         | _ -> 0m

      if IsToday evt.Data.Date then
         accrued + evt.Data.DebitedAmount
      else
         accrued

let applyEvent (state: Account) (evt: AccountEvent) =
   let newState =
      match evt with
      | BillingCycleStarted e -> {
         state with
            LastBillingCycleDate = Some e.Timestamp
            Events = []
        }
      | CreatedAccount e -> {
         empty with
            EntityId = e.EntityId
            Email = e.Data.Email
            FirstName = e.Data.FirstName
            LastName = e.Data.LastName
            Currency = e.Data.Currency
            Balance = e.Data.Balance
            Status = AccountStatus.Active
            MaintenanceFeeCriteria = MaintenanceFee.reset e.Data.Balance
        }
      | AccountEvent.AccountClosed _ -> {
         state with
            Status = AccountStatus.Closed
        }
      | DepositedCash e -> {
         state with
            Balance = state.Balance + e.Data.DepositedAmount
            MaintenanceFeeCriteria =
               MaintenanceFee.fromDeposit
                  state.MaintenanceFeeCriteria
                  e.Data.DepositedAmount
        }
      | DebitedAccount e ->
         let balance = state.Balance - e.Data.DebitedAmount

         {
            state with
               Balance = balance
               DailyDebitAccrued = dailyDebitAccrued state e
               LastDebitDate = Some e.Data.Date
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebit state.MaintenanceFeeCriteria balance
         }
      | MaintenanceFeeDebited e ->
         let balance = state.Balance - e.Data.DebitedAmount

         {
            state with
               Balance = balance
               MaintenanceFeeCriteria = MaintenanceFee.reset balance
         }
      | MaintenanceFeeSkipped _ -> {
         state with
            MaintenanceFeeCriteria = MaintenanceFee.reset state.Balance
        }
      | DailyDebitLimitUpdated e -> {
         state with
            DailyDebitLimit = e.Data.DebitLimit
        }
      | LockedCard _ -> { state with CardLocked = true }
      | UnlockedCard _ -> { state with CardLocked = false }
      | TransferPending e ->
         let balance = state.Balance - e.Data.DebitedAmount

         let state = {
            state with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebit state.MaintenanceFeeCriteria balance
               InProgressTransfers =
                  Map.add
                  <| string e.CorrelationId
                  <| TransferEventToTransaction.fromPending e
                  <| state.InProgressTransfers
         }

         match e.Data.Recipient.AccountEnvironment with
         | RecipientAccountEnvironment.Internal -> {
            state with
               DailyInternalTransferAccrued =
                  TransferLimits.accruedDailyTransfers state e
               LastInternalTransferDate = Some e.Data.Date
           }
         | RecipientAccountEnvironment.Domestic -> {
            state with
               DailyDomesticTransferAccrued =
                  TransferLimits.accruedDailyTransfers state e
               LastDomesticTransferDate = Some e.Data.Date
           }
      | TransferProgress e -> {
         state with
            // Map.add: will replace existing TransferTransaction
            // with the latest progress update.
            InProgressTransfers =
               Map.add
               <| string e.CorrelationId
               <| TransferEventToTransaction.fromProgressUpdate e
               <| state.InProgressTransfers
        }
      | TransferApproved e -> {
         state with
            InProgressTransfers =
               Map.remove (string e.CorrelationId) state.InProgressTransfers
        }
      | TransferRejected e ->
         let balance = state.Balance + e.Data.DebitedAmount

         // Updates status of transfer recipient when a transfer is declined
         // due to an account not existing or becoming closed.
         let updatedRecipients =
            match e.Data.Reason with
            | TransferDeclinedReason.InvalidAccountInfo
            | TransferDeclinedReason.AccountClosed ->
               let recipient = e.Data.Recipient
               let key = recipientLookupKey recipient
               state.TransferRecipients.Add(key, recipient)
            | _ -> state.TransferRecipients

         let state = {
            state with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebitReversal
                     state.MaintenanceFeeCriteria
                     balance
               InProgressTransfers =
                  Map.remove (string e.CorrelationId) state.InProgressTransfers
               TransferRecipients = updatedRecipients
         }

         match IsToday e.Data.Date, e.Data.Recipient.AccountEnvironment with
         | true, RecipientAccountEnvironment.Internal -> {
            state with
               DailyInternalTransferAccrued =
                  state.DailyInternalTransferAccrued - e.Data.DebitedAmount
           }
         | true, RecipientAccountEnvironment.Domestic -> {
            state with
               DailyDomesticTransferAccrued =
                  state.DailyDomesticTransferAccrued - e.Data.DebitedAmount
           }
         | _ -> state
      | TransferDeposited e -> {
         state with
            Balance = state.Balance + e.Data.DepositedAmount
            MaintenanceFeeCriteria =
               MaintenanceFee.fromDeposit
                  state.MaintenanceFeeCriteria
                  e.Data.DepositedAmount
        }
      | InternalTransferRecipient e ->
         let recipient = e.Data.toRecipient ()

         let key = recipientLookupKey recipient

         {
            state with
               TransferRecipients = state.TransferRecipients.Add(key, recipient)
         }
      | DomesticTransferRecipient e ->
         let recipient = e.Data.toRecipient ()

         let key = recipientLookupKey recipient

         {
            state with
               TransferRecipients = state.TransferRecipients.Add(key, recipient)
         }
      | InternalRecipientDeactivated e -> {
         state with
            TransferRecipients =
               Map.change
               <| string e.Data.RecipientId
               <| Option.map (fun acct -> {
                  acct with
                     Status = RecipientRegistrationStatus.Closed
               })
               <| state.TransferRecipients
        }
      | InternalSenderRegistered e -> {
         state with
            InternalTransferSenders =
               Map.add
                  e.Data.TransferSender.AccountId
                  e.Data.TransferSender
                  state.InternalTransferSenders
        }

   {
      newState with
         Events = evt :: newState.Events
   }

module private StateTransition =
   let transitionErr (err: StateTransitionError) =
      Error <| StateTransitionError err

   let map
      (eventTransform: BankEvent<'t> -> AccountEvent)
      (state: Account)
      (eventValidation: ValidationResult<BankEvent<'t>>)
      =
      eventValidation
      |> Result.mapError ValidationError
      |> Result.map (fun evt ->
         let evt = eventTransform evt
         (evt, applyEvent state evt))

   let create (state: Account) (cmd: CreateAccountCommand) =
      if state.Status <> AccountStatus.Pending then
         transitionErr AccountNotReadyToActivate
      else
         map CreatedAccount state (CreateAccountCommand.toEvent cmd)

   let startBillingcycle (state: Account) (cmd: StartBillingCycleCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map BillingCycleStarted state (StartBillingCycleCommand.toEvent cmd)

   let deposit (state: Account) (cmd: DepositCashCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map DepositedCash state (DepositCashCommand.toEvent cmd)

   let limitDailyDebits (state: Account) (cmd: LimitDailyDebitsCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map DailyDebitLimitUpdated state (LimitDailyDebitsCommand.toEvent cmd)

   let lockCard (state: Account) (cmd: LockCardCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map LockedCard state (LockCardCommand.toEvent cmd)

   let unlockCard (state: Account) (cmd: UnlockCardCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map UnlockedCard state (UnlockCardCommand.toEvent cmd)

   let debit (state: Account) (cmd: DebitCommand) =
      let input = cmd.Data

      if state.CardLocked then
         transitionErr AccountCardLocked
      elif state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif state.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance state.Balance
      elif
         IsToday input.Date
         && state.DailyDebitAccrued + input.Amount > state.DailyDebitLimit
      then
         transitionErr <| ExceededDailyDebit state.DailyDebitLimit
      else
         map DebitedAccount state (DebitCommand.toEvent cmd)

   let maintenanceFee (state: Account) (cmd: MaintenanceFeeCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif state.Balance - cmd.Data.Amount < 0m then
         transitionErr <| InsufficientBalance state.Balance
      else
         map MaintenanceFeeDebited state (MaintenanceFeeCommand.toEvent cmd)

   let skipMaintenanceFee (state: Account) (cmd: SkipMaintenanceFeeCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map MaintenanceFeeSkipped state (SkipMaintenanceFeeCommand.toEvent cmd)

   let transfer (state: Account) (cmd: TransferCommand) =
      let input = cmd.Data

      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif state.Balance - input.Amount < 0m then
         transitionErr <| InsufficientBalance state.Balance
      elif
         state.TransferRecipients
         |> Map.containsKey (recipientLookupKey input.Recipient)
         |> not
      then
         transitionErr RecipientRegistrationRequired
      elif TransferLimits.exceedsDailyTransferLimit state cmd then
         let err =
            match input.Recipient.AccountEnvironment with
            | RecipientAccountEnvironment.Internal ->
               ExceededDailyInternalTransferLimit
                  TransferLimits.DailyInternalLimit
            | RecipientAccountEnvironment.Domestic ->
               ExceededDailyDomesticTransferLimit
                  TransferLimits.DailyDomesticLimit

         transitionErr err
      else
         map TransferPending state (TransferCommand.toEvent cmd)

   let transferProgress (state: Account) (cmd: UpdateTransferProgressCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         let existing =
            Map.tryFind (string cmd.CorrelationId) state.InProgressTransfers

         match existing with
         | None ->
            map
               TransferProgress
               state
               (UpdateTransferProgressCommand.toEvent cmd)
         | Some txn ->
            let existingStatus = txn.Status

            if existingStatus = cmd.Data.Status then
               transitionErr TransferProgressNoChange
            else
               map
                  TransferProgress
                  state
                  (UpdateTransferProgressCommand.toEvent cmd)

   let approveTransfer (state: Account) (cmd: ApproveTransferCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind (string cmd.CorrelationId) state.InProgressTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map TransferApproved state (ApproveTransferCommand.toEvent cmd)

   let rejectTransfer (state: Account) (cmd: RejectTransferCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else if
         Option.isNone
         <| Map.tryFind (string cmd.CorrelationId) state.InProgressTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map TransferRejected state (RejectTransferCommand.toEvent cmd)

   let registerTransferRecipient
      (state: Account)
      (cmd: RegisterTransferRecipientCommand)
      =
      let recipient = cmd.Data.Recipient

      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif state.TransferRecipients.ContainsKey recipient.Identification then
         transitionErr RecipientAlreadyRegistered
      else
         match recipient.AccountEnvironment with
         | RecipientAccountEnvironment.Internal ->
            map InternalTransferRecipient state
            <| TransferRecipientEvent.local cmd
         | RecipientAccountEnvironment.Domestic ->
            map DomesticTransferRecipient state
            <| TransferRecipientEvent.domestic cmd

   let registerInternalSender
      (state: Account)
      (cmd: RegisterInternalSenderCommand)
      =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif
         state.InternalTransferSenders.ContainsKey cmd.Data.Sender.AccountId
      then
         transitionErr SenderAlreadyRegistered
      else
         map
            InternalSenderRegistered
            state
            (RegisterInternalSenderCommand.toEvent cmd)

   let deactivateInternalRecipient
      (state: Account)
      (cmd: DeactivateInternalRecipientCommand)
      =
      let recipientId = cmd.Data.RecipientId

      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif
         not <| Map.containsKey (string recipientId) state.TransferRecipients
      then
         transitionErr RecipientNotFound
      else
         let recipient = state.TransferRecipients[string recipientId]

         if recipient.Status = RecipientRegistrationStatus.Closed then
            transitionErr RecipientAlreadyDeactivated
         else
            map
               InternalRecipientDeactivated
               state
               (DeactivateInternalRecipientCommand.toEvent cmd)

   let depositTransfer (state: Account) (cmd: DepositTransferCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map TransferDeposited state <| (DepositTransferCommand.toEvent cmd)

   let closeAccount (state: Account) (cmd: CloseAccountCommand) =
      map AccountEvent.AccountClosed state (CloseAccountCommand.toEvent cmd)

let stateTransition (state: Account) (command: AccountCommand) =
   match command with
   | CreateAccount cmd -> StateTransition.create state cmd
   | StartBillingCycle cmd -> StateTransition.startBillingcycle state cmd
   | DepositCash cmd -> StateTransition.deposit state cmd
   | Debit cmd -> StateTransition.debit state cmd
   | MaintenanceFee cmd -> StateTransition.maintenanceFee state cmd
   | SkipMaintenanceFee cmd -> StateTransition.skipMaintenanceFee state cmd
   | LimitDailyDebits cmd -> StateTransition.limitDailyDebits state cmd
   | LockCard cmd -> StateTransition.lockCard state cmd
   | UnlockCard cmd -> StateTransition.unlockCard state cmd
   | Transfer cmd -> StateTransition.transfer state cmd
   | UpdateTransferProgress cmd -> StateTransition.transferProgress state cmd
   | ApproveTransfer cmd -> StateTransition.approveTransfer state cmd
   | RejectTransfer cmd -> StateTransition.rejectTransfer state cmd
   | DepositTransfer cmd -> StateTransition.depositTransfer state cmd
   | RegisterTransferRecipient cmd ->
      StateTransition.registerTransferRecipient state cmd
   | RegisterInternalSender cmd ->
      StateTransition.registerInternalSender state cmd
   | DeactivateInternalRecipient cmd ->
      StateTransition.deactivateInternalRecipient state cmd
   | CloseAccount cmd -> StateTransition.closeAccount state cmd
