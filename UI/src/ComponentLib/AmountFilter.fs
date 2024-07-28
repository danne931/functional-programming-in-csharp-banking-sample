module AmountFilter

open Feliz
open Fable.Core.JS

open Lib.NetworkQuery
open Lib.SharedTypes

let renderMoneyFlowFilter
   (direction: MoneyFlow option)
   (onChange: MoneyFlow option -> unit)
   =
   let options = [
      "any", "Any", direction.IsNone

      "in", "In (e.g. deposits, refunds)", direction = Some MoneyFlow.In

      "out", "Out (e.g. purchases, transfers)", direction = Some MoneyFlow.Out
   ]

   Html.fieldSet [
      for value, text, isChecked in options ->
         Html.label [
            Html.input [
               attr.type' "radio"
               attr.name "moneyFlow"
               attr.isChecked isChecked
               attr.ariaChecked isChecked
               attr.value value

               attr.onChange (fun (option: string) ->
                  let direction =
                     if option = "in" then Some MoneyFlow.In
                     else if option = "out" then Some MoneyFlow.Out
                     else None

                  onChange direction)
            ]
            Html.text text
         ]
   ]

[<ReactComponent>]
let AmountFilterComponent
   (amountFilterProp: AmountFilter option)
   (onChange: AmountFilter option -> unit)
   =
   let amountFilter, setFilter = React.useState amountFilterProp

   // Handle filter being removed from parent component.
   // (Filter Pill selected for deletion from table control panel component)
   React.useEffect (
      (fun () ->
         if amountFilter.IsSome && amountFilterProp.IsNone then
            setFilter None),
      [| box amountFilterProp |]
   )

   // Notify parent of change after a debounce.
   // (Updates browser URL query parameters & issues updated database query)
   React.useEffect (
      (fun () ->
         if amountFilter <> amountFilterProp then
            let timer = setTimeout (fun () -> onChange amountFilter) 1000
            React.createDisposable (fun _ -> clearTimeout timer)
         else
            React.createDisposable (fun _ -> ())),
      [| box amountFilter |]
   )

   Html.fieldSet [
      Html.label [
         Html.text "At least (≥)"

         Html.input [
            attr.type' "text"
            attr.name "amountMin"
            attr.placeholder "0.00"
            attr.onKeyDown keepPositiveNumbers

            attr.value (
               match amountFilter with
               | Some(AmountFilter.GreaterThanOrEqualTo min)
               | Some(AmountFilter.Between(min, _)) -> string min
               | _ -> ""
            )

            attr.onChange (fun (amount: string) ->
               let emptyInput = amount.Trim().Length = 0

               let filter =
                  match amountFilter with
                  | Some(AmountFilter.Between(_, max)) ->
                     if emptyInput then
                        Some <| AmountFilter.LessThanOrEqualTo max
                     else
                        Some <| AmountFilter.Between(decimal amount, max)
                  | Some(AmountFilter.LessThanOrEqualTo max) ->
                     if emptyInput then
                        amountFilter
                     else
                        Some <| AmountFilter.Between(decimal amount, max)
                  | _ ->
                     if emptyInput then
                        None
                     else
                        Some
                        <| AmountFilter.GreaterThanOrEqualTo(decimal amount)

               setFilter filter)
         ]
      ]

      Html.label [
         Html.text "No more than (≤)"

         Html.input [
            attr.type' "text"
            attr.name "amountMax"
            attr.placeholder "0.00"
            attr.onKeyDown keepPositiveNumbers

            attr.value (
               match amountFilter with
               | Some(AmountFilter.LessThanOrEqualTo max)
               | Some(AmountFilter.Between(_, max)) -> string max
               | _ -> ""
            )

            attr.onChange (fun (amount: string) ->
               let emptyInput = amount.Trim().Length = 0

               let filter =
                  match amountFilter with
                  | Some(AmountFilter.Between(min, _)) ->
                     if emptyInput then
                        Some <| AmountFilter.GreaterThanOrEqualTo(decimal min)
                     else
                        Some <| AmountFilter.Between(min, decimal amount)
                  | Some(AmountFilter.GreaterThanOrEqualTo min) ->
                     if emptyInput then
                        amountFilter
                     else
                        Some <| AmountFilter.Between(min, decimal amount)
                  | _ ->
                     if emptyInput then
                        None
                     else
                        Some <| AmountFilter.LessThanOrEqualTo(decimal amount)

               setFilter filter)
         ]
      ]
   ]
