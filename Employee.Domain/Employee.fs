[<RequireQualifiedAccess>]
module Employee

open Validus

open Bank.Employee.Domain
open Lib.SharedTypes
open Lib.Time

let dailyDebitAccrued (card: Card) (evt: BankEvent<DebitApproved>) : decimal =
   // When applying a new event to the cached Employee & the
   // last debit event did not occur today...
   // -> Ignore the cached DailyDebitAccrued
   let accrued =
      if
         card.LastDebitDate.IsSome && DateTime.isToday card.LastDebitDate.Value
      then
         card.DailyDebitAccrued
      else
         0m

   // When accumulating events into Employee aggregate...
   // -> Ignore debits older than a day
   let info = evt.Data.Info

   if DateTime.isToday info.Date then
      accrued + info.Amount
   else
      accrued

let applyEvent (state: Employee) (evt: EmployeeEvent) =
   match evt with
   | CreatedEmployee e -> {
      EmployeeId = EmployeeId.fromEntityId e.EntityId
      Role = e.Data.Role
      OrgId = e.OrgId
      Email = e.Data.Email
      FirstName = e.Data.FirstName
      LastName = e.Data.LastName
      Cards = Map.empty
      Status = EmployeeStatus.Active
      PendingPurchases = Map.empty
     }
   | CreatedCard e ->
      let info = e.Data.Info

      {
         state with
            Cards = state.Cards |> Map.add info.CardId info
      }
   | DebitRequested e -> {
      state with
         PendingPurchases =
            state.PendingPurchases |> Map.add e.CorrelationId e.Data.Info
     }
   | DebitApproved e ->
      let info = e.Data.Info

      {
         state with
            Cards =
               state.Cards
               |> Map.change
                     info.CardId
                     (Option.map (fun card -> {
                        card with
                           DailyDebitAccrued = dailyDebitAccrued card e
                           LastDebitDate = Some info.Date
                     }))
            PendingPurchases =
               state.PendingPurchases |> Map.remove e.CorrelationId
      }
   | DebitDeclined e -> {
      state with
         PendingPurchases = state.PendingPurchases |> Map.remove e.CorrelationId
     }
   | DailyDebitLimitUpdated e -> {
      state with
         Cards =
            state.Cards
            |> Map.change
                  e.Data.CardId
                  (Option.map (fun card -> {
                     card with
                        DailyDebitLimit = e.Data.DebitLimit
                  }))
     }
   | LockedCard e -> {
      state with
         Cards =
            state.Cards
            |> Map.change
                  e.Data.CardId
                  (Option.map (fun card -> { card with Locked = true }))
     }
   | UnlockedCard e -> {
      state with
         Cards =
            state.Cards
            |> Map.change
                  e.Data.CardId
                  (Option.map (fun card -> { card with Locked = false }))
     }

module private StateTransition =
   let transitionErr (err: EmployeeStateTransitionError) =
      Error <| EmployeeStateTransitionError err

   let map
      (eventTransform: BankEvent<'t> -> EmployeeEvent)
      (state: Employee)
      (eventValidation: ValidationResult<BankEvent<'t>>)
      =
      eventValidation
      |> Result.mapError ValidationError
      |> Result.map (fun evt ->
         let evt = eventTransform evt
         (evt, applyEvent state evt))

   let create (state: Employee) (cmd: CreateEmployeeCommand) =
      if state.Status <> EmployeeStatus.PendingApproval then
         transitionErr EmployeeNotReadyToActivate
      else
         map CreatedEmployee state (CreateEmployeeCommand.toEvent cmd)

   let createCard (state: Employee) (cmd: CreateCardCommand) =
      if state.Status <> EmployeeStatus.Active then
         transitionErr EmployeeNotActive
      else
         map CreatedCard state (CreateCardCommand.toEvent cmd)

   let limitDailyDebits (state: Employee) (cmd: LimitDailyDebitsCommand) =
      if state.Status <> EmployeeStatus.Active then
         transitionErr EmployeeNotActive
      else
         map DailyDebitLimitUpdated state (LimitDailyDebitsCommand.toEvent cmd)

   let lockCard (state: Employee) (cmd: LockCardCommand) =
      if state.Status <> EmployeeStatus.Active then
         transitionErr EmployeeNotActive
      else
         map LockedCard state (LockCardCommand.toEvent cmd)

   let unlockCard (state: Employee) (cmd: UnlockCardCommand) =
      if state.Status <> EmployeeStatus.Active then
         transitionErr EmployeeNotActive
      else
         map UnlockedCard state (UnlockCardCommand.toEvent cmd)

   let debitRequest (state: Employee) (cmd: DebitRequestCommand) =
      let info = cmd.Data

      if state.Status <> EmployeeStatus.Active then
         transitionErr EmployeeNotActive
      else
         match Map.tryFind info.CardId state.Cards with
         | None -> transitionErr CardNotFound
         | Some card when card.Locked -> transitionErr CardLocked
         | Some card when
            DateTime.isToday info.Date
            && card.DailyDebitAccrued + info.Amount > card.DailyDebitLimit
            ->
            transitionErr
            <| ExceededDailyDebit(card.DailyDebitLimit, card.DailyDebitAccrued)
         | Some _ -> map DebitRequested state (DebitRequestCommand.toEvent cmd)

   let approveDebit (state: Employee) (cmd: ApproveDebitCommand) =
      if state.Status <> EmployeeStatus.Active then
         transitionErr EmployeeNotActive
      elif
         Option.isNone <| Map.tryFind cmd.CorrelationId state.PendingPurchases
      then
         transitionErr DebitAlreadyProgressedToApprovedOrDeclined
      else
         map DebitApproved state (ApproveDebitCommand.toEvent cmd)

   let declineDebit (state: Employee) (cmd: DeclineDebitCommand) =
      if state.Status <> EmployeeStatus.Active then
         transitionErr EmployeeNotActive
      elif
         Option.isNone <| Map.tryFind cmd.CorrelationId state.PendingPurchases
      then
         transitionErr DebitAlreadyProgressedToApprovedOrDeclined
      else
         map DebitDeclined state (DeclineDebitCommand.toEvent cmd)

let stateTransition (state: Employee) (command: EmployeeCommand) =
   match command with
   | CreateEmployee cmd -> StateTransition.create state cmd
   | CreateCard cmd -> StateTransition.createCard state cmd
   | DebitRequest cmd -> StateTransition.debitRequest state cmd
   | ApproveDebit cmd -> StateTransition.approveDebit state cmd
   | DeclineDebit cmd -> StateTransition.declineDebit state cmd
   | LimitDailyDebits cmd -> StateTransition.limitDailyDebits state cmd
   | LockCard cmd -> StateTransition.lockCard state cmd
   | UnlockCard cmd -> StateTransition.unlockCard state cmd

let empty: Employee = {
   EmployeeId = EmployeeId System.Guid.Empty
   OrgId = OrgId System.Guid.Empty
   Role = EmployeeRole.Scholar
   Email = Email.empty
   FirstName = ""
   LastName = ""
   Status = EmployeeStatus.PendingApproval
   Cards = Map.empty
   PendingPurchases = Map.empty
}
