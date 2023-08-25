[<RequireQualifiedAccess>]
module Account

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
      let recipient =
         RegisterTransferRecipientEvent.eventToRecipient (
            e |> RegisteredInternalTransferRecipient
         )

      let key = recipientLookupKey recipient

      {
         state with
            TransferRecipients = state.TransferRecipients.Add(key, recipient)
      }
   | DomesticTransferRecipient e ->
      let recipient =
         RegisterTransferRecipientEvent.eventToRecipient (
            e |> RegisteredDomesticTransferRecipient
         )

      let key = recipientLookupKey recipient

      {
         state with
            TransferRecipients = state.TransferRecipients.Add(key, recipient)
      }
   | InternationalTransferRecipient e ->
      let recipient =
         RegisterTransferRecipientEvent.eventToRecipient (
            e |> RegisteredInternationalTransferRecipient
         )

      let key = recipientLookupKey recipient

      {
         state with
            TransferRecipients = state.TransferRecipients.Add(key, recipient)
      }

module private StateTransition =
   let deposit (state: AccountState) (cmd: DepositCashCommand) =
      if state.Status = AccountStatus.Closed then
         Error "AccountNotActive"
      elif cmd.Amount <= 0m then
         Error "InvalidDepositAmount"
      else
         let evt = DepositedCashEvent.create cmd |> DepositedCash
         Ok(evt, applyEvent state evt)

   let limitDailyDebits (state: AccountState) (cmd: LimitDailyDebitsCommand) =
      let evt = DailyDebitLimitUpdatedEvent.create cmd |> DailyDebitLimitUpdated

      Ok(evt, applyEvent state evt)

   let lockCard (state: AccountState) (cmd: LockCardCommand) =
      if state.Status <> AccountStatus.Active then
         Error "AccountNotActive"
      else
         let evt = LockedCardEvent.create cmd |> LockedCard
         Ok(evt, applyEvent state evt)

   let unlockCard (state: AccountState) (cmd: UnlockCardCommand) =
      if state.Status <> AccountStatus.ActiveWithLockedCard then
         Error $"Account card already unlocked {state.Status}"
      else
         let evt = UnlockedCardEvent.create cmd |> UnlockedCard
         Ok(evt, applyEvent state evt)

   let debit (state: AccountState) (cmd: DebitCommand) =
      if state.Status = AccountStatus.Closed then
         Error "AccountNotActive"
      elif state.Status = AccountStatus.ActiveWithLockedCard then
         Error "AccountCardLocked"
      elif state.Balance - cmd.Amount < state.AllowedOverdraft then
         Error "InsufficientBalance"
      elif
         state.DailyDebitLimit <> -1m
         && IsToday cmd.Timestamp
         && state.DailyDebitAccrued + cmd.Amount > state.DailyDebitLimit
      then
         Error $"ExceededDailyDebit {state.DailyDebitLimit}"
      else
         let evt = DebitedAccountEvent.create cmd |> DebitedAccount
         Ok(evt, applyEvent state evt)

   let maintenanceFee (state: AccountState) (cmd: MaintenanceFeeCommand) =
      if state.Status = AccountStatus.Closed then
         Error "AccountNotActive"
      elif state.Balance - cmd.Amount < state.AllowedOverdraft then
         Error "InsufficientBalance"
      else
         let evt = MaintenanceFeeEvent.create cmd |> MaintenanceFeeDebited
         Ok(evt, applyEvent state evt)

   let skipMaintenanceFee
      (state: AccountState)
      (cmd: SkipMaintenanceFeeCommand)
      =
      let evt = MaintenanceFeeEvent.reset cmd |> MaintenanceFeeSkipped
      Ok(evt, applyEvent state evt)

   let transfer (state: AccountState) (cmd: TransferCommand) =
      if state.Status = AccountStatus.Closed then
         Error "AccountNotActive"
      elif state.Balance - cmd.Amount < state.AllowedOverdraft then
         Error "InsufficientBalance"
      elif
         not
         <| state.TransferRecipients.ContainsKey(
            recipientLookupKey cmd.Recipient
         )
      then
         Error "RecipientRegistrationRequired"
      else
         let evt = TransferEvent.create cmd |> TransferPending
         Ok(evt, applyEvent state evt)

   let approveTransfer (state: AccountState) (cmd: ApproveTransferCommand) =
      let evt = TransferEvent.approve cmd |> TransferApproved

      Ok(evt, applyEvent state evt)

   let rejectTransfer (state: AccountState) (cmd: RejectTransferCommand) =
      let evt = TransferEvent.reject cmd |> TransferRejected

      Ok(evt, applyEvent state evt)

   let registerTransferRecipient
      (state: AccountState)
      (cmd: RegisterTransferRecipientCommand)
      =
      if state.TransferRecipients.ContainsKey cmd.Recipient.Identification then
         Error "RecipientAlreadyRegistered"
      else
         let evt =
            match cmd.Recipient.AccountEnvironment with
            | RecipientAccountEnvironment.Internal ->
               RegisterInternalTransferRecipientEvent.create cmd
               |> InternalTransferRecipient
            | RecipientAccountEnvironment.Domestic ->
               RegisterDomesticTransferRecipientEvent.create cmd
               |> DomesticTransferRecipient
            | RecipientAccountEnvironment.International ->
               RegisterInternationalTransferRecipientEvent.create cmd
               |> InternationalTransferRecipient

         Ok(evt, applyEvent state evt)

   let depositTransfer (state: AccountState) (cmd: DepositTransferCommand) =
      if state.Status = AccountStatus.Closed then
         Error "AccountNotActive"
      elif cmd.Amount <= 0m then
         Error "InvalidTransferDepositAmount"
      else
         let evt = TransferDepositedEvent.create cmd |> TransferDeposited
         Ok(evt, applyEvent state evt)

   let closeAccount (state: AccountState) (cmd: CloseAccountCommand) =
      let evt = AccountClosedEvent.create cmd |> AccountClosed
      Ok(evt, applyEvent state evt)

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
