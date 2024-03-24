module TransactionTable

open Feliz
open Fable.FontAwesome

open Bank.Account.UIDomain
open Bank.Account.Domain
open BillingStatement

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
   let showDiagnosticEvents, filterView = React.useState true

   let events =
      if showDiagnosticEvents then
         account.Events
      else
         account.Events
         |> billingTransactions
         |> List.map BillingTransaction.value

   Html.figure [
      classyNode Html.div [ "transaction-table-control-panel" ] [
         Html.a [
            attr.children [
               Fa.i [
                  if showDiagnosticEvents then
                     Fa.Solid.EyeSlash
                  else
                     Fa.Solid.Eye
               ] []
            ]

            attr.href ""

            attr.onClick (fun e ->
               e.preventDefault ()
               filterView <| not showDiagnosticEvents)

            attr.custom ("data-tooltip", "Toggle Diagnostic Display")
            attr.custom ("data-placement", "left")
         ]
      ]

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

            Html.tbody [ for txn in events -> renderTableRow txn ]
         ]
      ]
   ]
