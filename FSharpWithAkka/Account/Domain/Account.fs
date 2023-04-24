[<RequireQualifiedAccess>]
module Account

open System

open BankTypes
open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.Types

let streamName (id: Guid) = "accounts_" + id.ToString()

type AccountStatus =
   | Active = 0
   | ActiveWithLockedCard = 1
   | Closed = 2

type AccountState =
   {
      EntityId: Guid
      FirstName: string
      LastName: string
      Currency: string
      Status: AccountStatus
      Balance: decimal
      AllowedOverdraft: decimal
      DailyDebitLimit: decimal
      DailyDebitAccrued: decimal
      LastDebitDate: DateTime option
      TransferRecipients: Map<string, TransferRecipient>
   }

   member this.FullName = $"{this.FirstName} {this.LastName}"

// Move to lib folder
let IsToday (debitDate: DateTime) =
   let today = DateTime.UtcNow
   $"{today.Day}-{today.Month}-{today.Year}" = $"{debitDate.Day}-{debitDate.Month}-{debitDate.Year}"

module MonthlyMaintenanceFee =
   let Origin = "actor:maintenance_fee"
   let Amount = decimal 5
   let DailyBalanceThreshold = decimal 1500
   let QualifyingDeposit = decimal 250

let DailyDebitAccrued state (evt: BankEvent<DebitedAccount>) : decimal =
   // When accumulating events into AccountState aggregate...
   // -> Ignore debits older than a day
   if not <| IsToday evt.Timestamp then
      0
   elif evt.Data.Origin = MonthlyMaintenanceFee.Origin then
      state.DailyDebitAccrued
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
   AllowedOverdraft = 0
   DailyDebitLimit = -1
   DailyDebitAccrued = 0
   LastDebitDate = None
   TransferRecipients = Map.empty
}

let applyEvent (state: AccountState) (evt: AccountEvent) =
   match evt with
   | DepositedCash(e) -> {
      state with
         Balance = state.Balance + e.Data.DepositedAmount
     }
   | DebitedAccount(e) -> {
      state with
         Balance = state.Balance - e.Data.DebitedAmount
         DailyDebitAccrued = DailyDebitAccrued state e
         LastDebitDate = Some e.Timestamp
     }
   | DailyDebitLimitUpdated(e) -> {
      state with
         DailyDebitLimit = e.Data.DebitLimit
     }
   | LockedCard(_) -> {
      state with
         Status = AccountStatus.ActiveWithLockedCard
     }
   | UnlockedCard(_) -> {
      state with
         Status = AccountStatus.Active
     }
   | DebitedTransfer(e) -> {
      state with
         Balance = state.Balance - e.Data.DebitedAmount
     }
   | InternalTransferRecipient(e) -> {
      state with
         TransferRecipients =
            state.TransferRecipients.Add(
               e.Data.AccountNumber,
               RegisterTransferRecipientEvent.eventToRecipient (
                  e |> RegisteredInternalTransferRecipient
               )
            )
     }
   | DomesticTransferRecipient(e) -> {
      state with
         TransferRecipients =
            state.TransferRecipients.Add(
               $"{e.Data.RoutingNumber}_{e.Data.AccountNumber}",
               RegisterTransferRecipientEvent.eventToRecipient (
                  e |> RegisteredDomesticTransferRecipient
               )
            )
     }
   | InternationalTransferRecipient(e) -> {
      state with
         TransferRecipients =
            state.TransferRecipients.Add(
               e.Data.Identification,
               RegisterTransferRecipientEvent.eventToRecipient (
                  e |> RegisteredInternationalTransferRecipient
               )
            )
     }

module private StateTransition =
   let deposit state (cmd: DepositCashCommand) =
      if state.Status = AccountStatus.Closed then
         Error "AccountNotActive"
      elif cmd.Amount <= 0 then
         Error "InvalidDepositAmount"
      else
         let evt = DepositedCashEvent.create cmd |> DepositedCash
         Ok(evt, applyEvent state evt)

   let limitDailyDebits state (cmd: LimitDailyDebitsCommand) =
      let evt = DailyDebitLimitUpdatedEvent.create cmd |> DailyDebitLimitUpdated
      Ok(evt, applyEvent state evt)

   let lockCard state (cmd: LockCardCommand) =
      if state.Status <> AccountStatus.Active then
         Error "AccountNotActive"
      else
         let evt = LockedCardEvent.create cmd |> LockedCard
         Ok(evt, applyEvent state evt)

   let unlockCard state (cmd: UnlockCardCommand) =
      if state.Status <> AccountStatus.ActiveWithLockedCard then
         Error $"Account card already unlocked {state.Status}"
      else
         let evt = UnlockedCardEvent.create cmd |> UnlockedCard
         Ok(evt, applyEvent state evt)

   let debit state (cmd: DebitCommand) =
      if state.Status = AccountStatus.Closed then
         Error "AccountNotActive"
      elif state.Status = AccountStatus.ActiveWithLockedCard then
         Error "AccountCardLocked"
      elif state.Balance - cmd.Amount < state.AllowedOverdraft then
         Error "InsufficientBalance"
      elif
         state.DailyDebitLimit <> -1
         && IsToday cmd.Timestamp
         && state.DailyDebitAccrued + cmd.Amount > state.DailyDebitLimit
      then
         Error $"ExceededDailyDebit {state.DailyDebitLimit}"
      else
         let evt = (DebitedAccountEvent.create cmd) |> DebitedAccount
         Ok(evt, applyEvent state evt)

   let transfer state (cmd: TransferCommand) =
      if state.Status = AccountStatus.Closed then
         Error "AccountNotActive"
      elif state.Balance - cmd.Amount < state.AllowedOverdraft then
         Error "InsufficientBalance"
      elif
         not
         <| state.TransferRecipients.ContainsKey cmd.Recipient.Identification
      then
         Error "TransferErr.RecipientRegistrationRequired(cmd)"
      else
         let evt = TransferEvent.create cmd |> DebitedTransfer
         Ok(evt, applyEvent state evt)

   let registerTransferRecipient state (cmd: RegisterTransferRecipientCommand) =
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


let stateTransition state (command: AccountCommand) =
   match box command with
   | :? DepositCashCommand as cmd -> StateTransition.deposit state cmd
   | :? DebitCommand as cmd -> StateTransition.debit state cmd
   | :? LimitDailyDebitsCommand as cmd ->
      StateTransition.limitDailyDebits state cmd
   | :? LockCardCommand as cmd -> StateTransition.lockCard state cmd
   | :? UnlockCardCommand as cmd -> StateTransition.unlockCard state cmd
   | :? TransferCommand as cmd -> StateTransition.transfer state cmd
   | :? RegisterTransferRecipientCommand as cmd ->
      StateTransition.registerTransferRecipient state cmd
