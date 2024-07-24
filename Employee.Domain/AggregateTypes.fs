namespace Bank.Employee.Domain

open System

open Lib.SharedTypes

type EmployeeCommand =
   | CreateAccountOwner of CreateAccountOwnerCommand
   | CreateEmployee of CreateEmployeeCommand
   | CreateCard of CreateCardCommand
   | DebitRequest of DebitRequestCommand
   | ApproveDebit of ApproveDebitCommand
   | DeclineDebit of DeclineDebitCommand
   | LimitDailyDebits of LimitDailyDebitsCommand
   | LimitMonthlyDebits of LimitMonthlyDebitsCommand
   | LockCard of LockCardCommand
   | UnlockCard of UnlockCardCommand
   | UpdateRole of UpdateRoleCommand
   | EditCardNickname of EditCardNicknameCommand
   | CancelInvitation of CancelInvitationCommand
   | RefreshInvitationToken of RefreshInvitationTokenCommand
   | ConfirmInvitation of ConfirmInvitationCommand
   | ApproveInvitation of ApproveInvitationCommand
   | DenyInvitation of DenyInvitationCommand
   | RestoreAccess of RestoreAccessCommand

type EmployeeEvent =
   | CreatedAccountOwner of BankEvent<CreatedAccountOwner>
   | CreatedEmployee of BankEvent<CreatedEmployee>
   | CreatedCard of BankEvent<CreatedCard>
   | DebitRequested of BankEvent<DebitRequested>
   | DebitApproved of BankEvent<DebitApproved>
   | DebitDeclined of BankEvent<DebitDeclined>
   | DailyDebitLimitUpdated of BankEvent<DailyDebitLimitUpdated>
   | MonthlyDebitLimitUpdated of BankEvent<MonthlyDebitLimitUpdated>
   | LockedCard of BankEvent<LockedCard>
   | UnlockedCard of BankEvent<UnlockedCard>
   | UpdatedRole of BankEvent<RoleUpdated>
   | CardNicknamed of BankEvent<CardNicknamed>
   | InvitationCancelled of BankEvent<InvitationCancelled>
   | InvitationTokenRefreshed of BankEvent<InvitationTokenRefreshed>
   | InvitationConfirmed of BankEvent<InvitationConfirmed>
   | InvitationApproved of BankEvent<InvitationApproved>
   | InvitationDenied of BankEvent<InvitationDenied>
   | AccessRestored of BankEvent<AccessRestored>

type OpenEmployeeEventEnvelope = EmployeeEvent * Envelope

[<RequireQualifiedAccess>]
module EmployeeEnvelope =
   let get (evt: BankEvent<'E>) : Envelope = {
      Id = evt.Id
      EntityId = evt.EntityId
      OrgId = evt.OrgId
      CorrelationId = evt.CorrelationId
      InitiatedById = evt.InitiatedById
      Timestamp = evt.Timestamp
      EventName = evt.EventName
   }

   let wrap (o: BankEvent<_>) : EmployeeEvent =
      match box o with
      | :? BankEvent<CreatedAccountOwner> as evt -> CreatedAccountOwner evt
      | :? BankEvent<CreatedEmployee> as evt -> CreatedEmployee evt
      | :? BankEvent<CreatedCard> as evt -> CreatedCard evt
      | :? BankEvent<DebitRequested> as evt -> DebitRequested evt
      | :? BankEvent<DebitApproved> as evt -> DebitApproved evt
      | :? BankEvent<DebitDeclined> as evt -> DebitDeclined evt
      | :? BankEvent<DailyDebitLimitUpdated> as evt ->
         DailyDebitLimitUpdated evt
      | :? BankEvent<MonthlyDebitLimitUpdated> as evt ->
         MonthlyDebitLimitUpdated evt
      | :? BankEvent<LockedCard> as evt -> LockedCard evt
      | :? BankEvent<UnlockedCard> as evt -> UnlockedCard evt
      | :? BankEvent<RoleUpdated> as evt -> UpdatedRole evt
      | :? BankEvent<CardNicknamed> as evt -> CardNicknamed evt
      | :? BankEvent<InvitationCancelled> as evt -> InvitationCancelled evt
      | :? BankEvent<InvitationTokenRefreshed> as evt ->
         InvitationTokenRefreshed evt
      | :? BankEvent<InvitationConfirmed> as evt -> InvitationConfirmed evt
      | :? BankEvent<InvitationApproved> as evt -> InvitationApproved evt
      | :? BankEvent<InvitationDenied> as evt -> InvitationDenied evt
      | :? BankEvent<AccessRestored> as evt -> AccessRestored evt
      | _ -> failwith "Missing definition for EmployeeEvent message"

   let unwrap (o: EmployeeEvent) : OpenEmployeeEventEnvelope =
      match o with
      | CreatedAccountOwner evt -> wrap evt, get evt
      | CreatedEmployee evt -> wrap evt, get evt
      | CreatedCard evt -> wrap evt, get evt
      | DebitRequested evt -> wrap evt, get evt
      | DebitApproved evt -> wrap evt, get evt
      | DebitDeclined evt -> wrap evt, get evt
      | DailyDebitLimitUpdated evt -> wrap evt, get evt
      | MonthlyDebitLimitUpdated evt -> wrap evt, get evt
      | LockedCard evt -> wrap evt, get evt
      | UnlockedCard evt -> wrap evt, get evt
      | UpdatedRole evt -> wrap evt, get evt
      | CardNicknamed evt -> wrap evt, get evt
      | InvitationCancelled evt -> wrap evt, get evt
      | InvitationTokenRefreshed evt -> wrap evt, get evt
      | InvitationConfirmed evt -> wrap evt, get evt
      | InvitationApproved evt -> wrap evt, get evt
      | InvitationDenied evt -> wrap evt, get evt
      | AccessRestored evt -> wrap evt, get evt

type EmployeeMessage =
   | GetEmployee
   | StateChange of EmployeeCommand
   | Event of EmployeeEvent
   | Delete

type EmployeeHistory = {
   InitiatedByName: string
   EmployeeName: string
   Event: EmployeeEvent
}

type Employee = {
   EmployeeId: EmployeeId
   OrgId: OrgId
   Role: Role
   Email: Email
   FirstName: string
   LastName: string
   Cards: Map<CardId, Card>
   Status: EmployeeStatus
   PendingPurchases: Map<CorrelationId, DebitInfo>
   OnboardingTasks: EmployeeOnboardingTask list
   AuthProviderUserId: Guid option
} with

   member x.Name = $"{x.FirstName} {x.LastName}"

   member x.CompositeId = x.EmployeeId, x.OrgId

   member x.PendingAccessApproval =
      match x.Status with
      | EmployeeStatus.PendingInviteApproval
      | EmployeeStatus.PendingRestoreAccessApproval -> true
      | _ -> false

   member x.HasCard =
      x.Cards.Values
      |> Seq.exists (fun card ->
         not (card.IsExpired()) && card.Status <> CardStatus.Closed)

type EmployeeWithEvents = {
   Info: Employee
   Events: EmployeeEvent list
}

type CardWithMetrics = {
   Card: Card
   DailyPurchaseAccrued: decimal
   MonthlyPurchaseAccrued: decimal
   Employee: Employee
}
