namespace Bank.Employee.Domain

open Lib.SharedTypes

type EmployeeCommand =
   | CreateEmployee of CreateEmployeeCommand
   | CreateCard of CreateCardCommand
   | DebitRequest of DebitRequestCommand
   | ApproveDebit of ApproveDebitCommand
   | DeclineDebit of DeclineDebitCommand
   | LimitDailyDebits of LimitDailyDebitsCommand
   | LockCard of LockCardCommand
   | UnlockCard of UnlockCardCommand

type EmployeeEvent =
   | CreatedEmployee of BankEvent<CreatedEmployee>
   | CreatedCard of BankEvent<CreatedCard>
   | DebitRequested of BankEvent<DebitRequested>
   | DebitApproved of BankEvent<DebitApproved>
   | DebitDeclined of BankEvent<DebitDeclined>
   | DailyDebitLimitUpdated of BankEvent<DailyDebitLimitUpdated>
   | LockedCard of BankEvent<LockedCard>
   | UnlockedCard of BankEvent<UnlockedCard>

type OpenEmployeeEventEnvelope = EmployeeEvent * Envelope

[<RequireQualifiedAccess>]
module EmployeeEnvelope =
   let private get (evt: BankEvent<'E>) : Envelope = {
      Id = evt.Id
      EntityId = evt.EntityId
      OrgId = evt.OrgId
      CorrelationId = evt.CorrelationId
      Timestamp = evt.Timestamp
      EventName = evt.EventName
   }

   let wrap (o: BankEvent<_>) : EmployeeEvent =
      match box o with
      | :? BankEvent<CreatedEmployee> as evt -> CreatedEmployee evt
      | :? BankEvent<CreatedCard> as evt -> CreatedCard evt
      | :? BankEvent<DebitRequested> as evt -> DebitRequested evt
      | :? BankEvent<DebitApproved> as evt -> DebitApproved evt
      | :? BankEvent<DebitDeclined> as evt -> DebitDeclined evt
      | :? BankEvent<DailyDebitLimitUpdated> as evt ->
         DailyDebitLimitUpdated evt
      | :? BankEvent<LockedCard> as evt -> LockedCard evt
      | :? BankEvent<UnlockedCard> as evt -> UnlockedCard evt
      | _ -> failwith "Missing definition for EmployeeEvent message"

   let unwrap (o: EmployeeEvent) : OpenEmployeeEventEnvelope =
      match o with
      | CreatedEmployee evt -> wrap evt, get evt
      | CreatedCard evt -> wrap evt, get evt
      | DebitRequested evt -> wrap evt, get evt
      | DebitApproved evt -> wrap evt, get evt
      | DebitDeclined evt -> wrap evt, get evt
      | DailyDebitLimitUpdated evt -> wrap evt, get evt
      | LockedCard evt -> wrap evt, get evt
      | UnlockedCard evt -> wrap evt, get evt

type EmployeeMessage =
   | GetEmployee
   | GetEvents
   | StateChange of EmployeeCommand
   | Event of EmployeeEvent
   | Delete
