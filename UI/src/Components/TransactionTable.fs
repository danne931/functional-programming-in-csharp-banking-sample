module TransactionTable

open Feliz
open Feliz.UseElmish
open Elmish
open Fable.FontAwesome
open System

open Bank.Account.UIDomain
open Bank.Account.Domain
open BillingStatement
open AsyncUtil

type private Page = int

type State = {
   IsDiagnosticView: bool
   PaginatedBillingTransactions: Map<Page, Deferred<BillingTransactionsMaybe>>
   PageIndex: Page
}

type Msg =
   | ToggleDiagnosticView
   | ResetPageIndex
   | LoadBillingTransactions of
      accountId: Guid *
      Page *
      AsyncOperationStatus<BillingTransactionsMaybe>
   | Reset

let init () =
   {
      IsDiagnosticView = true
      PaginatedBillingTransactions = Map.empty
      PageIndex = 0
   },
   Cmd.none

let update msg state =
   match msg with
   | ToggleDiagnosticView ->
      {
         state with
            IsDiagnosticView = not state.IsDiagnosticView
      },
      Cmd.none
   | ResetPageIndex -> { state with PageIndex = 0 }, Cmd.none
   | LoadBillingTransactions(accountId, page, Started) ->
      let state = { state with PageIndex = page }

      let load = async {
         // UI page begins at 0 indicating events sourced from account state &
         // provided by parent component.
         // Page values >= 1 will be passed into this LoadBillingTransactions
         // message and account events will be sourced from billing records.
         // The billing statement postgres table expects offset values starting
         // at 0 so subtract 1 from the page before passing it to the service.
         let! res = AccountService.getPaginatedTransactions accountId (page - 1)
         return LoadBillingTransactions(accountId, page, Finished res)
      }

      let deferredPageData = state.PaginatedBillingTransactions.TryFind page

      match deferredPageData with
      | (Some(Deferred.Resolved(Ok _))) -> state, Cmd.none
      | _ ->
         {
            state with
               PaginatedBillingTransactions =
                  state.PaginatedBillingTransactions
                  |> Map.add page Deferred.InProgress
         },
         Cmd.fromAsync load
   | LoadBillingTransactions(_, page, Finished(Ok billingTxnsOpt)) ->
      let resolvedTxns = Deferred.Resolved(Ok billingTxnsOpt)

      {
         state with
            PaginatedBillingTransactions =
               state.PaginatedBillingTransactions
               |> Map.change page (Option.map (fun _ -> resolvedTxns))
      },
      Cmd.none
   | LoadBillingTransactions(accountId, page, Finished(Error err)) ->
      Log.error
         $"Error loading billing transactions for {accountId} - Page {page}: {err}"

      state, Cmd.none
   | Reset -> init ()

let renderControlPanel accountId state dispatch =
   let currPageData = state.PaginatedBillingTransactions.TryFind state.PageIndex

   let nextPageData =
      state.PaginatedBillingTransactions.TryFind(state.PageIndex + 1)

   let endOfPagination =
      match currPageData, nextPageData with
      | _, (Some(Deferred.Resolved(Ok None))) -> true
      | (Some(Deferred.Resolved(Ok None))), _ -> true
      | _, _ -> false

   classyNode Html.div [ "transaction-table-control-panel" ] [
      Html.a [
         attr.children [ Fa.i [ Fa.Solid.CaretRight ] [] ]

         attr.href ""

         attr.classes [
            "pagination"
            if endOfPagination then
               "secondary"
         ]

         if endOfPagination then
            attr.ariaDisabled true

         attr.onClick (fun e ->
            e.preventDefault ()

            if not endOfPagination then
               dispatch
               <| Msg.LoadBillingTransactions(
                  accountId,
                  state.PageIndex + 1,
                  Started
               ))
      ]

      Html.a [
         attr.children [ Fa.i [ Fa.Solid.CaretLeft ] [] ]

         attr.href ""

         attr.classes [
            "pagination"
            if state.PageIndex = 0 then
               "secondary"
         ]

         if state.PageIndex = 0 then
            attr.ariaDisabled true

         attr.onClick (fun e ->
            e.preventDefault ()

            if state.PageIndex = 1 then
               dispatch ResetPageIndex
            elif state.PageIndex > 1 then
               dispatch
               <| LoadBillingTransactions(
                  accountId,
                  state.PageIndex - 1,
                  Started
               ))
      ]

      Html.a [
         attr.children [
            Fa.i [
               if state.IsDiagnosticView then
                  Fa.Solid.EyeSlash
               else
                  Fa.Solid.Eye
            ] []
         ]

         attr.href ""

         if state.PageIndex <> 0 then
            attr.ariaDisabled true
            attr.classes [ "secondary" ]

         attr.onClick (fun e ->
            e.preventDefault ()

            if state.PageIndex = 0 then
               dispatch ToggleDiagnosticView)

         attr.custom ("data-tooltip", "Toggle Diagnostic Display")
         attr.custom ("data-placement", "left")
      ]
   ]

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

let renderTable (txns: AccountEvent list) =
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

         Html.tbody [ for txn in txns -> renderTableRow txn ]
      ]
   ]

[<ReactComponent>]
let TransactionTableComponent (accountOpt: AccountState option) =
   let state, dispatch = React.useElmish (init, update, [||])

   let accountIdOpt = accountOpt |> Option.map _.EntityId
   React.useEffect ((fun _ -> dispatch Reset), [| box accountIdOpt |])

   let loaderFinished = attr.value 100

   // Current account events are sourced from the account state.
   // If the user paginates (PageIndex > 0) then the
   // the events are sourced instead from historical billing
   // statement records.
   let showHistoricalBillingTxns = state.PageIndex > 0

   React.fragment [
      Html.progress [
         attr.style [ style.marginBottom 5 ]
         attr.custom ("data-transactions-loader", "")

         if showHistoricalBillingTxns then
            let deferredPageData =
               state.PaginatedBillingTransactions.TryFind state.PageIndex

            match deferredPageData with
            | (Some(Deferred.Resolved(Ok _))) -> loaderFinished
            | _ -> ()
         elif accountOpt.IsSome then
            loaderFinished
      ]

      match accountOpt with
      | None -> ()
      | Some account ->
         Html.figure [
            renderControlPanel account.EntityId state dispatch

            if showHistoricalBillingTxns then
               let deferredPageData =
                  state.PaginatedBillingTransactions.TryFind state.PageIndex

               match deferredPageData with
               | (Some(Deferred.Resolved(Ok(Some billingTxns)))) ->
                  billingTxns
                  |> List.map BillingTransaction.value
                  |> renderTable
               | (Some(Deferred.Resolved(Ok None))) ->
                  Html.small "No results found."
               | _ -> Html.none
            elif state.IsDiagnosticView then
               renderTable account.Events
            else
               account.Events
               |> billingTransactions
               |> List.map BillingTransaction.value
               |> renderTable
         ]
   ]
