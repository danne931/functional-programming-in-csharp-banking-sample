[<RequireQualifiedAccess>]
module Account

open Validus

open BankTypes
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
   // When accumulating events into AccountState aggregate...
   // -> Ignore debits older than a day
   if not <| IsToday evt.Timestamp then
      0m
   elif Option.isNone state.LastDebitDate then
      evt.Data.DebitedAmount
   // When applying a new event to the cached AccountState & the
   // last debit event did not occur today...
   // -> Ignore the cached DailyDebitAccrued
   elif not <| IsToday state.LastDebitDate.Value then
      evt.Data.DebitedAmount
   else
      state.DailyDebitAccrued + evt.Data.DebitedAmount

let applyEvent (state: AccountState) (evt: AccountEvent) =
   match evt with
   | CreatedAccount e ->
      MaintenanceFee.reset
         {
            AccountState.empty with
               EntityId = e.EntityId
               Email = e.Data.Email
               FirstName = e.Data.FirstName
               LastName = e.Data.LastName
               Currency = e.Data.Currency
               Balance = e.Data.Balance
               Status = AccountStatus.Active
         }
   | AccountClosed _ -> {
      state with
         Status = AccountStatus.Closed
     }
   | DepositedCash e ->
      MaintenanceFee.fromDeposit
         {
            state with
               Balance = state.Balance + e.Data.DepositedAmount
         }
         e.Data.DepositedAmount
   | DebitedAccount e ->
      MaintenanceFee.fromDebit
         {
            state with
               Balance = state.Balance - e.Data.DebitedAmount
               DailyDebitAccrued = DailyDebitAccrued state e
               LastDebitDate = Some e.Timestamp
         }
   | MaintenanceFeeDebited e ->
      MaintenanceFee.reset
         {
            state with
               Balance = state.Balance - e.Data.DebitedAmount
         }
   | MaintenanceFeeSkipped _ -> MaintenanceFee.reset state
   | DailyDebitLimitUpdated e -> {
      state with
         DailyDebitLimit = e.Data.DebitLimit
     }
   | LockedCard _ -> {
      state with
         Status = AccountStatus.ActiveWithLockedCard
     }
   | UnlockedCard _ -> {
      state with
         Status = AccountStatus.Active
     }
   | TransferPending e ->
      MaintenanceFee.fromDebit
         {
            state with
               Balance = state.Balance - e.Data.DebitedAmount
         }
   | TransferApproved _ -> state
   | TransferRejected e ->
      MaintenanceFee.fromDebitReversal
         {
            state with
               Balance = state.Balance + e.Data.DebitedAmount
         }
   | TransferDeposited e ->
      MaintenanceFee.fromDeposit
         {
            state with
               Balance = state.Balance + e.Data.DepositedAmount
         }
         e.Data.DepositedAmount
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
   | InternationalTransferRecipient e ->
      let recipient = e.Data.toRecipient ()
      let key = recipientLookupKey recipient

      {
         state with
            TransferRecipients = state.TransferRecipients.Add(key, recipient)
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

   let deposit (state: AccountState) (cmd: DepositCashCommand) =
      if state.Status = AccountStatus.Closed then
         transitionErr AccountNotActive
      else
         map DepositedCash state <| cmd.toEvent ()

   let limitDailyDebits (state: AccountState) (cmd: LimitDailyDebitsCommand) =
      map DailyDebitLimitUpdated state <| cmd.toEvent ()

   let lockCard (state: AccountState) (cmd: LockCardCommand) =
      if state.Status <> AccountStatus.Active then
         transitionErr AccountNotActive
      else
         map LockedCard state <| cmd.toEvent ()

   let unlockCard (state: AccountState) (cmd: UnlockCardCommand) =
      if state.Status <> AccountStatus.ActiveWithLockedCard then
         transitionErr AccountCardAlreadyUnlocked
      else
         map UnlockedCard state <| cmd.toEvent ()

   let debit (state: AccountState) (cmd: DebitCommand) =
      if state.Status = AccountStatus.Closed then
         transitionErr AccountNotActive
      elif state.Status = AccountStatus.ActiveWithLockedCard then
         transitionErr AccountCardLocked
      elif state.Balance - cmd.Amount < state.AllowedOverdraft then
         transitionErr <| InsufficientBalance state.Balance
      elif
         state.DailyDebitLimit <> -1m
         && IsToday cmd.Timestamp
         && state.DailyDebitAccrued + cmd.Amount > state.DailyDebitLimit
      then
         transitionErr <| ExceededDailyDebit state.DailyDebitLimit
      else
         map DebitedAccount state <| cmd.toEvent ()

   let maintenanceFee (state: AccountState) (cmd: MaintenanceFeeCommand) =
      if state.Status = AccountStatus.Closed then
         transitionErr AccountNotActive
      elif state.Balance - cmd.Amount < state.AllowedOverdraft then
         transitionErr <| InsufficientBalance state.Balance
      else
         map MaintenanceFeeDebited state <| cmd.toEvent ()

   let skipMaintenanceFee
      (state: AccountState)
      (cmd: SkipMaintenanceFeeCommand)
      =
      map MaintenanceFeeSkipped state <| cmd.toEvent ()

   let transfer (state: AccountState) (cmd: TransferCommand) =
      if state.Status = AccountStatus.Closed then
         transitionErr AccountNotActive
      elif state.Balance - cmd.Amount < state.AllowedOverdraft then
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

   let approveTransfer (state: AccountState) (cmd: ApproveTransferCommand) =
      map TransferApproved state <| cmd.toEvent ()

   let rejectTransfer (state: AccountState) (cmd: RejectTransferCommand) =
      map TransferRejected state <| cmd.toEvent ()

   let registerTransferRecipient
      (state: AccountState)
      (cmd: RegisterTransferRecipientCommand)
      =
      if state.TransferRecipients.ContainsKey cmd.Recipient.Identification then
         transitionErr RecipientAlreadyRegistered
      else
         match cmd.Recipient.AccountEnvironment with
         | RecipientAccountEnvironment.Internal ->
            map InternalTransferRecipient state
            <| TransferRecipientEvent.local cmd
         | RecipientAccountEnvironment.Domestic ->
            map DomesticTransferRecipient state
            <| TransferRecipientEvent.domestic cmd
         | RecipientAccountEnvironment.International ->
            map InternationalTransferRecipient state
            <| TransferRecipientEvent.international cmd

   let depositTransfer (state: AccountState) (cmd: DepositTransferCommand) =
      if state.Status = AccountStatus.Closed then
         transitionErr AccountNotActive
      else
         map TransferDeposited state <| cmd.toEvent ()

   let closeAccount (state: AccountState) (cmd: CloseAccountCommand) =
      map AccountClosed state <| cmd.toEvent ()

let stateTransition (state: AccountState) (command: Command) =
   match box command with
   | :? DepositCashCommand as cmd -> StateTransition.deposit state cmd
   | :? DebitCommand as cmd -> StateTransition.debit state cmd
   | :? MaintenanceFeeCommand as cmd -> StateTransition.maintenanceFee state cmd
   | :? SkipMaintenanceFeeCommand as cmd ->
      StateTransition.skipMaintenanceFee state cmd
   | :? LimitDailyDebitsCommand as cmd ->
      StateTransition.limitDailyDebits state cmd
   | :? LockCardCommand as cmd -> StateTransition.lockCard state cmd
   | :? UnlockCardCommand as cmd -> StateTransition.unlockCard state cmd
   | :? TransferCommand as cmd -> StateTransition.transfer state cmd
   | :? ApproveTransferCommand as cmd ->
      StateTransition.approveTransfer state cmd
   | :? RejectTransferCommand as cmd -> StateTransition.rejectTransfer state cmd
   | :? DepositTransferCommand as cmd ->
      StateTransition.depositTransfer state cmd
   | :? RegisterTransferRecipientCommand as cmd ->
      StateTransition.registerTransferRecipient state cmd
   | :? CloseAccountCommand as cmd -> StateTransition.closeAccount state cmd
