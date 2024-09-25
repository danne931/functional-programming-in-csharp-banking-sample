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

[<RequireQualifiedAccess>]
type TransactionGroupFilter =
   | Purchase
   | Deposit
   | InternalTransferWithinOrg
   | InternalTransferBetweenOrgs
   | InternalAutomatedTransfer
   | DomesticTransfer
   | PlatformPayment
   //| ThirdPartyPayment

   member x.Display =
      match x with
      | TransactionGroupFilter.Purchase -> "Purchases"
      | TransactionGroupFilter.Deposit -> "Deposits"
      | TransactionGroupFilter.InternalTransferWithinOrg ->
         "Transfers within your org"
      | TransactionGroupFilter.InternalTransferBetweenOrgs ->
         "Transfers between orgs on the platform"
      | TransactionGroupFilter.InternalAutomatedTransfer ->
         "Automated balance management transfers"
      | TransactionGroupFilter.DomesticTransfer ->
         "Domestic transfers outside the platform"
      | TransactionGroupFilter.PlatformPayment ->
         "Payments between orgs on the platform"
//| TransactionGroupFilter.ThirdPartyPayment ->
//   "Payments outside the platform"

module TransactionGroupFilter =
   let fromString =
      function
      | "Purchase" -> Some TransactionGroupFilter.Purchase
      | "Deposit" -> Some TransactionGroupFilter.Deposit
      | "InternalTransferWithinOrg" ->
         Some TransactionGroupFilter.InternalTransferWithinOrg
      | "InternalTransferBetweenOrgs" ->
         Some TransactionGroupFilter.InternalTransferBetweenOrgs
      | "InternalAutomatedTransfer" ->
         Some TransactionGroupFilter.InternalAutomatedTransfer
      | "DomesticTransfer" -> Some TransactionGroupFilter.DomesticTransfer
      | "PlatformPayment" -> Some TransactionGroupFilter.PlatformPayment
      //| "ThirdPartyPayment" -> Some TransactionGroupFilter.ThirdPartyPayment
      | _ -> None

   let fromQueryString: string -> TransactionGroupFilter list option =
      listFromQueryString fromString

   let listToDisplay (items: TransactionGroupFilter list) =
      List.fold
         (fun acc (filter: TransactionGroupFilter) ->
            if acc = "" then
               filter.Display
            else
               $"{acc}, {filter.Display}")
         ""
         items

type TransactionQuery = {
   AccountId: AccountId
   Diagnostic: bool
   Page: int
   MoneyFlow: MoneyFlow option
   Category: CategoryFilter option
   Amount: AmountFilter option
   DateRange: (DateTime * DateTime) option
   CardIds: (CardId list) option
   InitiatedByIds: (InitiatedById list) option
   EventType: (TransactionGroupFilter list) option
}

module TransactionQuery =
   let cardIdsFromQueryString: string -> CardId list option =
      listFromQueryString (Guid.parseOptional >> Option.map CardId)

   let initiatedByIdsFromQueryString: string -> InitiatedById list option =
      listFromQueryString (
         Guid.parseOptional >> Option.map (EmployeeId >> InitiatedById)
      )
