namespace Bank.Account.Domain

open System

open Lib.SharedTypes
open Lib.NetworkQuery

[<RequireQualifiedAccess>]
type Currency =
   | USD
   | EUR
   | THB
   | VND

[<RequireQualifiedAccess>]
type AccountStatus =
   | InitialEmptyState
   | Pending
   | Active
   | Closed
   | ReadyForDelete

   override x.ToString() =
      match x with
      | AccountStatus.InitialEmptyState -> "initialemptystate"
      | AccountStatus.Pending -> "pending"
      | AccountStatus.Active -> "active"
      | AccountStatus.Closed -> "closed"
      | AccountStatus.ReadyForDelete -> "readyfordelete"

   static member fromString(status: string) : AccountStatus option =
      if String.IsNullOrEmpty status then
         None
      else
         match status.ToLower() with
         | "pending" -> Some AccountStatus.Pending
         | "active" -> Some AccountStatus.Active
         | "closed" -> Some AccountStatus.Closed
         | "readyfordelete" -> Some AccountStatus.ReadyForDelete
         | _ -> None

   static member fromStringUnsafe(status: string) : AccountStatus =
      match AccountStatus.fromString status with
      | None -> failwith "Error attempting to cast string to AccountStatus"
      | Some status -> status

[<RequireQualifiedAccess>]
type AccountDepository =
   | Checking
   | Savings

   override x.ToString() =
      match x with
      | AccountDepository.Checking -> "checking"
      | AccountDepository.Savings -> "savings"

   static member fromString(dep: string) : AccountDepository option =
      if String.IsNullOrEmpty dep then
         None
      else
         match dep.ToLower() with
         | "checking" -> Some AccountDepository.Checking
         | "savings" -> Some AccountDepository.Savings
         | _ -> None

   static member fromStringUnsafe(dep: string) : AccountDepository =
      match AccountDepository.fromString dep with
      | None -> failwith "Error attempting to cast string to AccountDepository"
      | Some dep -> dep

type TransactionCategory = { Id: int; Name: string }

type Merchant = {
   OrgId: OrgId
   Name: string
   Alias: string option
}

type OrgPermissions = {
   RequiresEmployeeInviteApproval: bool
   SocialTransferDiscoveryPrimaryAccountId: AccountId option
}

type Org = {
   OrgId: OrgId
   Name: string
   Permissions: OrgPermissions
}

[<RequireQualifiedAccess>]
type MoneyFlow =
   | In
   | Out

module MoneyFlow =
   let fromString (flow: string) : MoneyFlow option =
      if String.IsNullOrEmpty flow then
         None
      else
         match flow.ToLower() with
         | "in" -> Some MoneyFlow.In
         | "out" -> Some MoneyFlow.Out
         | _ -> None

type TransactionQuery = {
   AccountId: AccountId
   Diagnostic: bool
   Page: int
   MoneyFlow: MoneyFlow option
   Category: CategoryFilter option
   Amount: AmountFilter option
   DateRange: (DateTime * DateTime) option
   CardIds: (CardId list) option
}

module TransactionQuery =
   let cardIdsFromQueryString: string -> CardId list option =
      listFromQueryString (Guid.parseOptional >> Option.map CardId)
