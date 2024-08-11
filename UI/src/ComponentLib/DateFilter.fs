module DateFilter

open Feliz
open System

open UIDomain

let renderDateFilterSelect
   (dateFilter: DateFilter option)
   (onChange: DateFilter option -> unit)
   (includeCustomOption: bool)
   =
   Html.select [
      attr.placeholder "None"

      attr.value (
         match dateFilter with
         | None -> ""
         | Some(DateFilter.Custom(_, _)) -> "Custom"
         | Some filter -> string filter
      )

      attr.onChange (fun (dateFilterId: string) ->
         let selected =
            match dateFilterId.ToLower() with
            | "last30days" -> Some DateFilter.Last30Days
            | "currentmonth" -> Some DateFilter.CurrentMonth
            | "lastmonth" -> Some DateFilter.LastMonth
            | "currentyear" -> Some DateFilter.CurrentYear
            | "lastyear" -> Some DateFilter.LastYear
            | "custom" ->
               // Provide custom date range input with default values without
               // updating the URL query params.  Once the user edits the
               // custom date range then the URL query params will update.
               dateFilter
               |> Option.map DateFilter.toDateRange
               |> Option.defaultValue (
                  DateTime.Today - TimeSpan.FromDays 15,
                  DateTime.Today
               )
               |> DateFilter.Custom
               |> Some
            | filterId ->
               Log.error $"Missing implementation for date filter {filterId}"

               None

         onChange selected)

      attr.children [
         match dateFilter with
         | None ->
            Html.option [
               attr.disabled true
               attr.value ""
               attr.text "-- No selection --"
            ]
         | _ -> ()

         Html.option [
            attr.value (string DateFilter.Last30Days)
            attr.text "Last 30 days"
         ]

         if includeCustomOption then
            Html.option [ attr.value "Custom"; attr.text "Custom" ]

         Html.option [
            attr.value (string DateFilter.CurrentMonth)
            attr.text "This month"
         ]

         Html.option [
            attr.value (string DateFilter.LastMonth)
            attr.text "Last month"
         ]

         Html.option [
            attr.value (string DateFilter.CurrentYear)
            attr.text "This year"
         ]

         Html.option [
            attr.value (string DateFilter.LastYear)
            attr.text "Last year"
         ]
      ]
   ]

[<ReactComponent>]
let DateFilterComponent
   (dateFilter: DateFilter option)
   (onChange: DateFilter option -> unit)
   =
   let dateFilter, setDateFilter = React.useState dateFilter

   React.fragment [
      Html.label [ Html.text "Date Range:" ]

      renderDateFilterSelect
         dateFilter
         (fun selected ->
            setDateFilter selected

            match selected with
            | Some(DateFilter.Custom _) -> ()
            | _ -> onChange selected)
         true

      match dateFilter with
      | Some(DateFilter.Custom(startDate, endDate)) ->
         CustomDateRange.CustomDateRangeComponent
            startDate
            endDate
            (fun (dateStart, dateEnd) ->
               DateFilter.Custom(dateStart, dateEnd) |> Some |> onChange)
      | _ -> ()
   ]
