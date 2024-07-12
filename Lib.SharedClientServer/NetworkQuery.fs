module Lib.NetworkQuery

open System

open Lib.Time
open Lib.SharedTypes

let listToQueryString (items: 'T list) =
   List.fold
      (fun acc filter ->
         if acc = "" then string filter else $"{acc},{string filter}")
      ""
      items

let listFromQueryString
   (parseOptional: string -> 'T option)
   (query: string)
   : 'T list option
   =
   if String.IsNullOrWhiteSpace query then
      None
   else
      query.Split(",")
      |> List.ofArray
      |> List.choose parseOptional
      |> fun items -> if items.IsEmpty then None else Some items

let dateRangeFromQueryString (dateStr: string) : (DateTime * DateTime) option =
   if String.IsNullOrWhiteSpace dateStr then
      None
   else
      let dateRange = dateStr.Split ","

      if dateRange.Length = 2 then
         Option.map2
            (fun startDate endDate -> startDate, endDate)
            (DateTime.parseOptional dateRange.[0])
            (DateTime.parseOptional dateRange.[1])
      else
         None

[<RequireQualifiedAccess>]
type CategoryFilter =
   | IsCategorized of bool
   | CategoryIds of int list

module CategoryFilter =
   let categoryFromQueryString (query: string) : CategoryFilter option =
      listFromQueryString
         (fun str ->
            try
               Some(int str)
            with _ ->
               None)
         query
      |> Option.map CategoryFilter.CategoryIds

[<RequireQualifiedAccess>]
type AmountFilter =
   | LessThanOrEqualTo of decimal
   | GreaterThanOrEqualTo of decimal
   | Between of decimal * decimal

type TransactionQuery = {
   AccountId: AccountId
   Diagnostic: bool
   Page: int
   MoneyFlow: MoneyFlow option
   Category: CategoryFilter option
   Amount: AmountFilter option
   DateRange: (DateTime * DateTime) option
}

[<RequireQualifiedAccess>]
type EmployeeEventGroupFilter =
   | Invitation
   | CreatedCard
   | Purchase
   | DailyDebitLimitUpdated
   | CardFrozenUnfrozen
   | UpdatedRole
   | AccessRestored

   member x.Display =
      match x with
      | EmployeeEventGroupFilter.Invitation -> "Invitations"
      | EmployeeEventGroupFilter.CreatedCard -> "Cards Issued"
      | EmployeeEventGroupFilter.Purchase -> "Purchases"
      | EmployeeEventGroupFilter.DailyDebitLimitUpdated ->
         "Purchase Limit Applied"
      | EmployeeEventGroupFilter.CardFrozenUnfrozen -> "Card Frozen/Unfrozen"
      | EmployeeEventGroupFilter.UpdatedRole -> "Employee Role Altered"
      | EmployeeEventGroupFilter.AccessRestored -> "Employee Access Restored"

module EmployeeEventGroupFilter =
   let fromString =
      function
      | "Invitation" -> Some EmployeeEventGroupFilter.Invitation
      | "CreatedCard" -> Some EmployeeEventGroupFilter.CreatedCard
      | "Purchase" -> Some EmployeeEventGroupFilter.Purchase
      | "DailyDebitLimitUpdated" ->
         Some EmployeeEventGroupFilter.DailyDebitLimitUpdated
      | "CardFrozenUnfrozen" -> Some EmployeeEventGroupFilter.CardFrozenUnfrozen
      | "UpdatedRole" -> Some EmployeeEventGroupFilter.UpdatedRole
      | "AccessRestored" -> Some EmployeeEventGroupFilter.AccessRestored
      | _ -> None

   let fromQueryString: string -> EmployeeEventGroupFilter list option =
      listFromQueryString fromString

   let listToDisplay (items: EmployeeEventGroupFilter list) =
      List.fold
         (fun acc (filter: EmployeeEventGroupFilter) ->
            if acc = "" then
               filter.Display
            else
               $"{acc}, {filter.Display}")
         ""
         items

type EmployeeHistoryQuery = {
   Page: int
   DateRange: (DateTime * DateTime) option
   EventType: (EmployeeEventGroupFilter list) option
   EmployeeIds: (EmployeeId list) option
   InitiatedByIds: (InitiatedById list) option
}

module EmployeeHistoryQuery =
   let employeeIdsFromQueryString: string -> EmployeeId list option =
      listFromQueryString (Guid.parseOptional >> Option.map EmployeeId)

   let initiatedByIdsFromQueryString: string -> InitiatedById list option =
      listFromQueryString (
         Guid.parseOptional >> Option.map (EmployeeId >> InitiatedById)
      )

type EmployeeQuery = {
   EmployeeIds: (EmployeeId list) option
   Roles: (Role list) option
}

module EmployeeQuery =
   let rolesFromQueryString = listFromQueryString Role.fromString

   let rolesToDisplay =
      List.fold
         (fun acc (filter: Role) ->
            if acc = "" then
               filter.Display
            else
               $"{acc}, {filter.Display}")
         ""

   let employeeIdsFromQueryString =
      EmployeeHistoryQuery.employeeIdsFromQueryString
