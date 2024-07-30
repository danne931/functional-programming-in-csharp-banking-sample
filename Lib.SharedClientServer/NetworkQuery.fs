module Lib.NetworkQuery

open System

open Lib.Time

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

module AmountFilter =
   let fromQuery (amountMin: Nullable<decimal>) (amountMax: Nullable<decimal>) =
      match Option.ofNullable amountMin, Option.ofNullable amountMax with
      | Some min, None -> Some(AmountFilter.GreaterThanOrEqualTo min)
      | None, Some max -> Some(AmountFilter.LessThanOrEqualTo max)
      | Some min, Some max -> Some(AmountFilter.Between(min, max))
      | _ -> None
