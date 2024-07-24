namespace Bank.Employee.Domain

open System

open Lib.SharedTypes

[<RequireQualifiedAccess>]
type CardType =
   | Credit
   | Debit

   static member fromString(status: string) : CardType option =
      if String.IsNullOrEmpty status then
         None
      else
         match status.ToLower() with
         | "credit" -> Some Credit
         | "debit" -> Some Debit
         | _ -> None

   static member fromStringUnsafe(cardType: string) : CardType =
      match CardType.fromString cardType with
      | None -> failwith "Error attempting to cast string to CardType"
      | Some status -> status

[<RequireQualifiedAccess>]
type CardStatus =
   //| Pending
   | Active
   | Frozen
   | Closed

   override x.ToString() =
      match x with
      | Active -> "active"
      | Frozen -> "frozen"
      | Closed -> "closed"

   static member fromString(status: string) : CardStatus option =
      if String.IsNullOrEmpty status then
         None
      else
         match status.ToLower() with
         | "active" -> Some Active
         | "frozen" -> Some Frozen
         | "closed" -> Some Closed
         | _ -> None

   static member fromStringUnsafe(status: string) : CardStatus =
      match CardStatus.fromString status with
      | None -> failwith "Error attempting to cast string to CardStatus"
      | Some status -> status

type CardExpiration = { Month: int; Year: int }

module CardExpiration =
   let create () : CardExpiration =
      let exp = DateTime.Now.AddYears(3)
      { Month = exp.Month; Year = exp.Year }

type Card = {
   CardType: CardType
   CardNumberLast4: string
   DailyPurchaseLimit: decimal
   MonthlyPurchaseLimit: decimal
   Virtual: bool // virtual vs. physical card
   Status: CardStatus
   CardNickname: string option
   LastPurchaseAt: DateTime option
   Expiration: CardExpiration
   CardId: CardId
   AccountId: AccountId
} with

   member x.IsExpired() =
      DateTime(x.Expiration.Year, x.Expiration.Month, 1) <= DateTime.UtcNow

   member x.Display =
      $"""
      {x.CardNickname |> Option.defaultValue ""}
      **{x.CardNumberLast4}
      """

type EmployeeInviteSupplementaryCardInfo = {
   DailyPurchaseLimit: decimal
   MonthlyPurchaseLimit: decimal
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
   CardNumberLast4: string
   Date: DateTime
   Amount: decimal
   Origin: string
   Reference: string option
}

/// Tasks to initiate upon employee invite confirmation.
type EmployeeOnboardingTask = CreateCard of EmployeeInviteSupplementaryCardInfo
