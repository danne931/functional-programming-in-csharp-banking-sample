[<RequireQualifiedAccess>]
module Account

open Validus

open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.Types
open Lib.Time

let recipientLookupKey (recipient: TransferRecipient) =
   match recipient.AccountEnvironment with
   | RecipientAccountEnvironment.Internal -> recipient.Identification
   | RecipientAccountEnvironment.Domestic ->
      $"{recipient.RoutingNumber.Value}_{recipient.Identification}"

let DailyDebitAccrued state (evt: BankEvent<DebitedAccount>) : decimal =
   // When applying a new event to the cached AccountState & the
   // last debit event did not occur today...
   // -> Ignore the cached DailyDebitAccrued
   let dailyDebitAccrued =
      if state.LastDebitDate.IsSome && IsToday state.LastDebitDate.Value then
         state.DailyDebitAccrued
      else
         0m

   // When accumulating events into AccountState aggregate...
   // -> Ignore debits older than a day
   if IsToday evt.Data.Date then
      dailyDebitAccrued + evt.Data.DebitedAmount
   else
      dailyDebitAccrued

let applyEvent (state: AccountState) (evt: AccountEvent) =
   let newState =
      match evt with
      | CreatedAccount e -> {
         AccountState.empty with
            EntityId = e.EntityId
            Email = e.Data.Email
            FirstName = e.Data.FirstName
            LastName = e.Data.LastName
            Currency = e.Data.Currency
            Balance = e.Data.Balance
            Status = AccountStatus.Active
            MaintenanceFeeCriteria = MaintenanceFee.reset e.Data.Balance
        }
      | AccountClosed _ -> {
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
               DailyDebitAccrued = DailyDebitAccrued state e
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
      | LockedCard _ -> {
         state with
            Status = AccountStatus.CardLocked
        }
      | UnlockedCard _ -> {
         state with
            Status = AccountStatus.Active
        }
      | TransferPending e ->
         let balance = state.Balance - e.Data.DebitedAmount

         {
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

         {
            state with
               Balance = balance
               MaintenanceFeeCriteria =
                  MaintenanceFee.fromDebitReversal
                     state.MaintenanceFeeCriteria
                     balance
               InProgressTransfers =
                  Map.remove (string e.CorrelationId) state.InProgressTransfers
         }
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

   {
      newState with
         Events = evt :: state.Events
   }

module private StateTransition =
   let transitionErr (err: StateTransitionError) =
      Error <| StateTransitionError err

   let map
      (eventTransform: BankEvent<'t> -> AccountEvent)
      (state: AccountState)
      (eventValidation: ValidationResult<BankEvent<'t>>)
      =
      eventValidation
      |> Result.mapError ValidationError
      |> Result.map (fun evt ->
         let evt = eventTransform evt
         (evt, applyEvent state evt))

   let create (state: AccountState) (cmd: CreateAccountCommand) =
      if state.Status <> AccountStatus.ReadyToOpen then
         transitionErr AccountNotReadyToActivate
      else
         map CreatedAccount state <| cmd.toEvent ()

   let deposit (state: AccountState) (cmd: DepositCashCommand) =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      else
         map DepositedCash state <| cmd.toEvent ()

   let limitDailyDebits (state: AccountState) (cmd: LimitDailyDebitsCommand) =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      else
         map DailyDebitLimitUpdated state <| cmd.toEvent ()

   let lockCard (state: AccountState) (cmd: LockCardCommand) =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      else
         map LockedCard state <| cmd.toEvent ()

   let unlockCard (state: AccountState) (cmd: UnlockCardCommand) =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      else
         map UnlockedCard state <| cmd.toEvent ()

   let debit (state: AccountState) (cmd: DebitCommand) =
      if state.Status = AccountStatus.CardLocked then
         transitionErr AccountCardLocked
      elif state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      elif state.Balance - cmd.Amount < 0m then
         transitionErr <| InsufficientBalance state.Balance
      elif
         state.DailyDebitLimit <> -1m
         && IsToday cmd.Date
         && state.DailyDebitAccrued + cmd.Amount > state.DailyDebitLimit
      then
         transitionErr <| ExceededDailyDebit state.DailyDebitLimit
      else
         map DebitedAccount state <| cmd.toEvent ()

   let maintenanceFee (state: AccountState) (cmd: MaintenanceFeeCommand) =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      elif state.Balance - cmd.Amount < 0m then
         transitionErr <| InsufficientBalance state.Balance
      else
         map MaintenanceFeeDebited state <| cmd.toEvent ()

   let skipMaintenanceFee
      (state: AccountState)
      (cmd: SkipMaintenanceFeeCommand)
      =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      else
         map MaintenanceFeeSkipped state <| cmd.toEvent ()

   let transfer (state: AccountState) (cmd: TransferCommand) =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      elif state.Balance - cmd.Amount < 0m then
         transitionErr <| InsufficientBalance state.Balance
      elif
         not
         <| state.TransferRecipients.ContainsKey(
            recipientLookupKey cmd.Recipient
         )
      then
         transitionErr RecipientRegistrationRequired
      else
         map TransferPending state <| cmd.toEvent ()

   let transferProgress
      (state: AccountState)
      (cmd: UpdateTransferProgressCommand)
      =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      else
         let existing =
            Map.tryFind (string cmd.CorrelationId) state.InProgressTransfers

         match existing with
         | None -> map TransferProgress state <| cmd.toEvent ()
         | Some txn ->
            let existingStatus = txn.Status

            if existingStatus = cmd.Status then
               transitionErr TransferProgressNoChange
            else
               map TransferProgress state <| cmd.toEvent ()

   let approveTransfer (state: AccountState) (cmd: ApproveTransferCommand) =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      else if
         Option.isNone
         <| Map.tryFind (string cmd.CorrelationId) state.InProgressTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map TransferApproved state <| cmd.toEvent ()

   let rejectTransfer (state: AccountState) (cmd: RejectTransferCommand) =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      else if
         Option.isNone
         <| Map.tryFind (string cmd.CorrelationId) state.InProgressTransfers
      then
         transitionErr TransferAlreadyProgressedToApprovedOrRejected
      else
         map TransferRejected state <| cmd.toEvent ()

   let registerTransferRecipient
      (state: AccountState)
      (cmd: RegisterTransferRecipientCommand)
      =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      elif
         state.TransferRecipients.ContainsKey cmd.Recipient.Identification
      then
         transitionErr RecipientAlreadyRegistered
      else
         match cmd.Recipient.AccountEnvironment with
         | RecipientAccountEnvironment.Internal ->
            map InternalTransferRecipient state
            <| TransferRecipientEvent.local cmd
         | RecipientAccountEnvironment.Domestic ->
            map DomesticTransferRecipient state
            <| TransferRecipientEvent.domestic cmd

   let depositTransfer (state: AccountState) (cmd: DepositTransferCommand) =
      if not state.CanProcessTransactions then
         transitionErr AccountTransactionProcessingDisabled
      else
         map TransferDeposited state <| cmd.toEvent ()

   let closeAccount (state: AccountState) (cmd: CloseAccountCommand) =
      map AccountClosed state <| cmd.toEvent ()

let stateTransition (state: AccountState) (command: AccountCommand) =
   match command with
   | CreateAccount cmd -> StateTransition.create state cmd
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
   | CloseAccount cmd -> StateTransition.closeAccount state cmd
