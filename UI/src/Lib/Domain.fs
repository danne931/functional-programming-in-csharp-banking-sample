module UIDomain

open System

open Lib.NetworkQuery
open Lib.Time

module CategoryFilter =
   let display
      (categories: Map<int, Bank.Account.Domain.TransactionCategory>)
      (filter: CategoryFilter)
      =
      match filter with
      | CategoryFilter.IsCategorized isCat ->
         Some <| if isCat then "Categorized" else "Uncategorized"
      | CategoryFilter.CategoryIds catIds ->
         let size = catIds.Length

         if size > 1 then
            Some $"Categories ({size})"
         else
            categories |> Map.tryFind catIds.Head |> Option.map _.Name

module AmountFilter =
   let display =
      function
      | AmountFilter.GreaterThanOrEqualTo amount -> $"≥ ${amount}"
      | AmountFilter.LessThanOrEqualTo amount -> $"≤ ${amount}"
      | AmountFilter.Between(amountStart, amountEnd) ->
         $"${amountStart} - ${amountEnd}"

   let fromQueryString (queryParams: Map<string, string>) =
      let findAmount key =
         Map.tryFind key queryParams |> Option.map decimal

      let min = findAmount "amountMin"
      let max = findAmount "amountMax"

      match min, max with
      | Some min, None -> Some(AmountFilter.GreaterThanOrEqualTo min)
      | None, Some max -> Some(AmountFilter.LessThanOrEqualTo max)
      | Some min, Some max -> Some(AmountFilter.Between(min, max))
      | _ -> None

   let toQuery (amount: AmountFilter) =
      match amount with
      | AmountFilter.GreaterThanOrEqualTo amount -> [
         "amountMin", string amount
        ]
      | AmountFilter.LessThanOrEqualTo amount -> [ "amountMax", string amount ]
      | AmountFilter.Between(min, max) -> [
         "amountMin", string min
         "amountMax", string max
        ]

type MoneyFlow = Bank.Account.Domain.MoneyFlow

module MoneyFlow =
   let display =
      function
      | MoneyFlow.In -> "Money in"
      | MoneyFlow.Out -> "Money out"

[<RequireQualifiedAccess>]
type DateFilter =
   | Custom of dateStart: DateTime * dateEnd: DateTime
   | Last30Days
   | CurrentMonth
   | LastMonth
   | CurrentYear
   | LastYear

module DateFilter =
   let toDateRange (filter: DateFilter) =
      let endOfToday = DateTime.Today.AddDays(1).AddMilliseconds(-1)

      match filter with
      | DateFilter.Custom(startDate, endDate) -> startDate, endDate
      | DateFilter.Last30Days -> DateTime.Today.AddDays(-30), endOfToday
      | DateFilter.CurrentMonth ->
         DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), endOfToday
      | DateFilter.LastMonth ->
         let start = DateTime.Today.AddMonths -1

         let endDate =
            DateTime(
               start.Year,
               start.Month,
               DateTime.DaysInMonth(start.Year, start.Month)
            )

         DateTime(start.Year, start.Month, 1),
         endDate.AddDays(1).AddMilliseconds(-1)
      | DateFilter.CurrentYear ->
         DateTime(DateTime.Today.Year, 1, 1), endOfToday
      | DateFilter.LastYear ->
         DateTime(DateTime.Today.AddYears(-1).Year, 1, 1),
         DateTime(DateTime.Today.Year, 1, 1).AddMilliseconds(-1)

   let toQueryString (filter: DateFilter) =
      filter
      |> toDateRange
      |> fun (st, en) -> DateTime.rangeAsQueryString st en

   let dateRangeDisplay (dates: DateTime * DateTime) =
      let dateStart, dateEnd = DateTime.formatRangeShort (fst dates) (snd dates)
      $"{dateStart} - {dateEnd}"

   let fromString =
      function
      | "Last30Days" -> Some DateFilter.Last30Days
      | "CurrentMonth" -> Some DateFilter.CurrentMonth
      | "LastMonth" -> Some DateFilter.LastMonth
      | "CurrentYear" -> Some DateFilter.CurrentYear
      | "LastYear" -> Some DateFilter.LastYear
      | str -> dateRangeFromQueryString str |> Option.map DateFilter.Custom

let dateUIFriendly (date: DateTime) =
   let dayAndMonth = date.ToLongDateString().Split(string date.Year)[0]
   $"{dayAndMonth} {date.ToShortTimeString()}"
