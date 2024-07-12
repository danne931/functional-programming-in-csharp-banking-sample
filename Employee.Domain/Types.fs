namespace Bank.Employee.Domain

open System

open Lib.SharedTypes

type CardNumber =
   | CardNumber of int64

   override x.ToString() =
      let (CardNumber num) = x
      string num

   member x.Last4 =
      x |> string |> (fun str -> str.Substring(str.Length - 4)) |> int

type CVV = CVV of int16

type CardSecurityInfo = {
   PersonName: string
   Expiration: DateTime
   CVV: CVV
   CardNumber: CardNumber
}

[<RequireQualifiedAccess>]
type CardStatus =
   //| Pending
   | Active
   | Frozen
   | Closed

type Card = {
   SecurityInfo: CardSecurityInfo
   CardNickname: string option
   DailyDebitLimit: decimal
   DailyDebitAccrued: decimal
   LastDebitDate: DateTime option
   CardId: CardId
   AccountId: AccountId
   Virtual: bool // virtual vs. physical card
   Status: CardStatus
} with

   member x.IsExpired() =
      x.SecurityInfo.Expiration <= DateTime.UtcNow

type EmployeeInviteSupplementaryCardInfo = {
   DailyPurchaseLimit: decimal
   LinkedAccountId: AccountId
}

type InviteToken = {
   Token: Guid
   Expiration: DateTime
} with

   member x.IsExpired() =
      x.Expiration >= DateTime.UtcNow.AddDays 7.

module InviteToken =
   let generate () = {
      Token = Guid.NewGuid()
      Expiration = DateTime.UtcNow.AddDays 7.
   }

[<RequireQualifiedAccess>]
type EmployeeStatus =
   | InitialEmptyState
   | PendingInviteApproval
   | PendingInviteConfirmation of InviteToken
   | Active
   | Closed
   | PendingRestoreAccessApproval
   | ReadyForDelete

   member x.Display =
      match x with
      | InitialEmptyState -> ""
      | PendingInviteConfirmation _ -> "Pending Invite Confirmation"
      | PendingInviteApproval -> "Pending Invite Approval"
      | Active -> "Active"
      | Closed -> "Closed"
      | PendingRestoreAccessApproval -> "Pending Restore Approval"
      | ReadyForDelete -> "Processing Removal"

   override x.ToString() =
      match x with
      | InitialEmptyState -> "initialemptystate"
      | PendingInviteApproval -> "pendinginviteapproval"
      | PendingInviteConfirmation _ -> "pendinginviteconfirmation"
      | Active -> "active"
      | Closed -> "closed"
      | PendingRestoreAccessApproval -> "pendingrestoreaccessapproval"
      | ReadyForDelete -> "readyfordelete"

type DebitInfo = {
   AccountId: AccountId
   EmployeeId: EmployeeId
   CorrelationId: CorrelationId
   CardId: CardId
   CardNumberLast4: int
   Date: DateTime
   Amount: decimal
   Origin: string
   Reference: string option
}

/// Tasks to initiate upon employee invite confirmation.
type EmployeeOnboardingTask = CreateCard of EmployeeInviteSupplementaryCardInfo

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
