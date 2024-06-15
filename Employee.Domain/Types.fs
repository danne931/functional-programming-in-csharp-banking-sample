namespace Bank.Employee.Domain

open System

open Lib.SharedTypes

type CardNumber =
   | CardNumber of int64

   override x.ToString() =
      let (CardNumber num) = x
      string num

   member x.Last4 = x |> string |> (fun str -> str.Substring(str.Length - 4))

type CVV = CVV of int16

type CardSecurityInfo = {
   PersonName: string
   Expiration: DateTime
   CVV: CVV
   CardNumber: CardNumber
}

type Card = {
   SecurityInfo: CardSecurityInfo
   CardNickname: string option
   DailyDebitLimit: decimal
   DailyDebitAccrued: decimal
   LastDebitDate: DateTime option
   Locked: bool
   CardId: CardId
   AccountId: AccountId
   Virtual: bool // virtual vs. physical card
}

type EmployeeRole =
   | Admin
   | CardOnly
   | Scholar
//| Custom of CustomEmployeeBehaviors

type EmployeeStatus =
   | PendingApproval
   | Active
   | Closed
   | ReadyForDelete

module EmployeeStatus =
   let fromString (status: string) : EmployeeStatus option =
      match status.ToLower() with
      | "pendingapproval" -> Some EmployeeStatus.PendingApproval
      | "active" -> Some EmployeeStatus.Active
      | "closed" -> Some EmployeeStatus.Closed
      | "readyfordelete" -> Some EmployeeStatus.ReadyForDelete
      | _ -> None

   let fromStringUnsafe (status: string) : EmployeeStatus =
      match fromString status with
      | None -> failwith "Error attempting to cast string to EmployeeStatus"
      | Some status -> status

type DebitInfo = {
   AccountId: AccountId
   EmployeeId: EmployeeId
   CorrelationId: CorrelationId
   CardId: CardId
   Date: DateTime
   Amount: decimal
   Origin: string
   Reference: string option
}

type Employee = {
   EmployeeId: EmployeeId
   OrgId: OrgId
   Role: EmployeeRole
   Email: Email
   FirstName: string
   LastName: string
   Cards: Map<CardId, Card>
   Status: EmployeeStatus
   PendingPurchases: Map<CorrelationId, DebitInfo>
} with

   member x.Name = $"{x.FirstName} {x.LastName}"

   member x.CompositeId = x.EmployeeId, x.OrgId
