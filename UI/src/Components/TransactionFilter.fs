module TransactionFilterViews

open Feliz

open Lib.TransactionQuery
open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Account.UIDomain

[<RequireQualifiedAccess>]
type TransactionFilterView =
   | Date
   | Amount
   | Category

let private views =
   Map [
      TransactionFilterView.Date, "Date"
      TransactionFilterView.Amount, "Amount"
      TransactionFilterView.Category, "Categories"
   ]

[<RequireQualifiedAccess>]
type TransactionFilter =
   | Date of DateFilter option
   | MoneyFlow of MoneyFlow option
   | Amount of AmountFilter option
   | Category of CategoryFilter option

let private renderFilterMenu selectedView setView =
   Html.ul [
      for view, name in Map.toSeq views ->
         Html.li [
            Html.a [
               attr.href "#"
               attr.text name

               attr.onClick (fun e ->
                  e.preventDefault ()

                  if selectedView <> view then
                     setView view)

               if selectedView = view then
                  attr.ariaDisabled true
                  attr.classes [ "secondary" ]
                  attr.style [ style.cursor.defaultCursor ]
            ]
         ]
   ]

let renderTransactionFilters
   (query: AccountBrowserQuery)
   (categories: Map<int, TransactionCategory>)
   (view: TransactionFilterView)
   (onViewSelect: TransactionFilterView -> unit)
   (onChange: TransactionFilter -> unit)
   (onClose: unit -> unit)
   =
   Html.div [
      attr.onKeyDown (fun e ->
         e.stopPropagation ()

         if e.key = "Escape" then
            onClose ())

      attr.children [
         classyNode Html.div [ "grid"; "filter-view" ] [
            Html.aside [
               CloseButton.render (fun _ -> onClose ())

               renderFilterMenu view onViewSelect
            ]

            Html.section [
               match view with
               | TransactionFilterView.Category ->
                  TransactionCategoryFilter.TransactionCategoryFilterComponent
                     query.Category
                     categories
                     (TransactionFilter.Category >> onChange)
               | TransactionFilterView.Amount ->
                  TransactionAmountFilter.renderMoneyFlowFilter
                     query.MoneyFlow
                     (TransactionFilter.MoneyFlow >> onChange)

                  TransactionAmountFilter.AmountFilterComponent
                     query.Amount
                     (TransactionFilter.Amount >> onChange)
               | TransactionFilterView.Date ->
                  TransactionDateFilter.TransactionDateFilterComponent
                     query.Date
                     (TransactionFilter.Date >> onChange)
            ]
         ]
      ]
   ]
