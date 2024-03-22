module TransactionTable

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish
open System

open AsyncUtil
open Bank.Account.UIDomain
open Bank.Account.Domain

[<RequireQualifiedAccess>]
type TableView =
   | Diagnostic
   | UserFriendly

type State = {
   Account: AccountState
   View: TableView
}

type Msg = SelectTableView of TableView

let init account =
   {
      Account = account
      View = TableView.Diagnostic
   },
   Cmd.none

let update msg state =
   match msg with
   | SelectTableView view ->
      let state = { state with View = view }
      { state with View = view }, Cmd.none

let renderTableRow (txn: AccountEvent) =
   let _, envelope = AccountEnvelope.unwrap txn

   let txn = transactionUIFriendly txn
   let orDefaultValue opt = opt |> Option.defaultValue "-"

   Html.tr [
      attr.key envelope.Id

      attr.children [
         Html.th [ attr.scope "row" ]

         Html.td [
            attr.classes [
               match txn.MoneyFlow with
               | MoneyFlow.None -> ""
               | MoneyFlow.In -> "credit"
               | MoneyFlow.Out -> "debit"
            ]

            attr.text (txn.Amount |> orDefaultValue)
         ]

         Html.td txn.Name

         Html.td (txn.Origin |> orDefaultValue)

         Html.td txn.Date

         Html.td (txn.Info |> orDefaultValue)
      ]
   ]

[<ReactComponent>]
let TransactionTableComponent (account: AccountState) =
   let state, dispatch = React.useElmish (init account, update, [||])

   Html.figure [
      Html.table [
         attr.role "grid"
         attr.children [
            Html.thead [
               Html.tr [
                  Html.th [ attr.scope "col" ]

                  Html.th [ attr.scope "col"; attr.text "Amount" ]

                  Html.th [ attr.scope "col"; attr.text "Event" ]

                  Html.th [ attr.scope "col"; attr.text "Origin" ]

                  Html.th [ attr.scope "col"; attr.text "Date" ]

                  Html.th [ attr.scope "col"; attr.text "Info" ]
               ]
            ]

            Html.tbody [ for txn in account.Events -> renderTableRow txn ]
         ]
      ]
   ]
