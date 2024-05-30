module Lib.TransactionQuery

open System

open Lib.Time
open Lib.SharedTypes

[<RequireQualifiedAccess>]
type CategoryFilter =
   | IsCategorized of bool
   | CategoryIds of int list

[<RequireQualifiedAccess>]
type AmountFilter =
   | LessThanOrEqualTo of decimal
   | GreaterThanOrEqualTo of decimal
   | Between of decimal * decimal

type TransactionQuery = {
   AccountId: AccountId
   Diagnostic: bool
   Page: int
   MoneyFlow: Lib.SharedTypes.MoneyFlow option
   Category: CategoryFilter option
   Amount: AmountFilter option
   DateRange: (DateTime * DateTime) option
}

module TransactionQuery =
   let dateRangeFromQueryString
      (dateStr: string)
      : (DateTime * DateTime) option
      =
      let dateRange = dateStr.Split ","

      if dateRange.Length = 2 then
         Option.map2
            (fun startDate endDate -> startDate, endDate)
            (DateTime.parseOptional dateRange.[0])
            (DateTime.parseOptional dateRange.[1])
      else
         None

   let categoryListToQueryString (catIds: int list) : string =
      List.fold
         (fun acc id -> if acc = "" then string id else $"{acc},{string id}")
         ""
         catIds

   let categoryFromQueryString (categoryIdsQuery: string) =
      categoryIdsQuery.Split(",")
      |> List.ofArray
      |> List.map int
      |> CategoryFilter.CategoryIds
