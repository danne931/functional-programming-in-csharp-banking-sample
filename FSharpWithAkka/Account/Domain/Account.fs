[<RequireQualifiedAccess>]
module Account

open System

open BankTypes
open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.Types
open Lib.Time

let streamName (id: Guid) = "accounts_" + string id

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

let create (e: BankEvent<CreatedAccount>) = {
   EntityId = e.EntityId
   FirstName = e.Data.FirstName
   LastName = e.Data.LastName
   Currency = e.Data.Currency
   Balance = e.Data.Balance
   Status = AccountStatus.Active
   AllowedOverdraft = 0m
   DailyDebitLimit = -1m
   DailyDebitAccrued = 0m
   LastDebitDate = None
   TransferRecipients = Map.empty
}

let applyEvent (state: AccountState) (evt: AccountEvent) =
   match evt with
   | DepositedCash e -> {
      state with
         Balance = state.Balance + e.Data.DepositedAmount
     }
   | DebitedAccount e -> {
      state with
         Balance = state.Balance - e.Data.DebitedAmount
         DailyDebitAccrued = DailyDebitAccrued state e
         LastDebitDate = Some e.Timestamp
     }
   | MaintenanceFeeDebited e -> {
      state with
         Balance = state.Balance - e.Data.DebitedAmount
     }
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
   | TransferPending e -> {
      state with
         Balance = state.Balance - e.Data.DebitedAmount
     }
   | TransferApproved _ -> state
   | TransferRejected e -> {
      state with
         Balance = state.Balance + e.Data.DebitedAmount
     }
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
   | _ -> state

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
         Error "TransferErr.RecipientRegistrationRequired(cmd)"
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
         Error "TransferErr.RecipientAlreadyRegistered(cmd)"
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

let stateTransition (state: AccountState) (command: Command) =
   match box command with
   | :? DepositCashCommand as cmd -> StateTransition.deposit state cmd
   | :? DebitCommand as cmd -> StateTransition.debit state cmd
   | :? MaintenanceFeeCommand as cmd -> StateTransition.maintenanceFee state cmd
   | :? LimitDailyDebitsCommand as cmd ->
      StateTransition.limitDailyDebits state cmd
   | :? LockCardCommand as cmd -> StateTransition.lockCard state cmd
   | :? UnlockCardCommand as cmd -> StateTransition.unlockCard state cmd
   | :? TransferCommand as cmd -> StateTransition.transfer state cmd
   | :? ApproveTransferCommand as cmd ->
      StateTransition.approveTransfer state cmd
   | :? RejectTransferCommand as cmd -> StateTransition.rejectTransfer state cmd
   | :? RegisterTransferRecipientCommand as cmd ->
      StateTransition.registerTransferRecipient state cmd

let initialAccountStateFromEventHistory events =
   let (CreatedAccount createdEvt) = List.head events
   create createdEvt

let foldEventsIntoAccount events =
   List.fold
      applyEvent
      (initialAccountStateFromEventHistory events)
      (List.tail events)
