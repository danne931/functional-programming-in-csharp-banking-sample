module TransactionTable

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish
open Fable.FontAwesome

open Lib.SharedTypes
open Lib.Time
open Bank.Account.UIDomain
open Bank.Account.Domain
open AsyncUtil
open Lib.TransactionQuery
open TransactionFilterViews

type State = {
   FilterView: TransactionFilterView option
   Transactions: Map<int, Deferred<TransactionsMaybe>>
   TransactionQuery: TransactionQuery
}

type Msg =
   | ToggleDiagnosticView
   | ToggleFilterView
   | UpdateFilter of TransactionFilter
   | SelectFilterView of TransactionFilterView
   | ResetPageIndex
   | LoadTransactions of
      TransactionQuery *
      AsyncOperationStatus<TransactionsMaybe>
   | TransactionPropsResolved of Deferred<TransactionsMaybe>
   | ViewTransaction of EventId

let init
   (txnsDeferred: Deferred<TransactionsMaybe>)
   (txnQuery: TransactionQuery)
   ()
   =
   {
      FilterView = None
      Transactions = Map [ 1, txnsDeferred ]
      TransactionQuery = txnQuery
   },
   Cmd.none

let update msg state =
   match msg with
   | ToggleDiagnosticView ->
      let query = {
         state.TransactionQuery with
            Page = 1
            Diagnostic = not state.TransactionQuery.Diagnostic
      }

      {
         state with
            TransactionQuery = query
            Transactions = Map [ 1, Deferred.Idle ]
      },
      Cmd.ofMsg (Msg.LoadTransactions(query, Started))
   | ToggleFilterView ->
      {
         state with
            FilterView =
               match state.FilterView with
               | None -> Some TransactionFilterView.Date
               | Some _ -> None
      },
      Cmd.none
   | UpdateFilter filter ->
      let query = { state.TransactionQuery with Page = 1 }

      let query =
         match filter with
         | TransactionFilter.Date filter -> {
            query with
               DateRange = filter |> Option.map DateFilter.toDateRange
           }
         | TransactionFilter.MoneyFlow direction -> {
            query with
               MoneyFlow = direction
           }
         | TransactionFilter.Amount amount -> { query with Amount = amount }
         | TransactionFilter.Category cat -> { query with Category = cat }

      let browserQuery = Routes.IndexUrl.accountBrowserQuery ()

      let browserQuery =
         match filter with
         | TransactionFilter.Date filter -> { browserQuery with Date = filter }
         | TransactionFilter.MoneyFlow direction -> {
            browserQuery with
               MoneyFlow = direction
           }
         | TransactionFilter.Amount amount -> {
            browserQuery with
               Amount = amount
           }
         | TransactionFilter.Category cat -> {
            browserQuery with
               Category = cat
           }

      let browserQueryParams =
         browserQuery
         |> AccountBrowserQuery.toQueryParams
         |> Router.encodeQueryString

      {
         state with
            TransactionQuery = query
            Transactions = Map [ 1, Deferred.Idle ]
      },
      Cmd.batch [
         Cmd.ofMsg (Msg.LoadTransactions(query, Started))
         Cmd.navigate ("account", string query.AccountId, browserQueryParams)
      ]
   | SelectFilterView view -> { state with FilterView = Some view }, Cmd.none
   | ResetPageIndex ->
      let query = {
         state.TransactionQuery with
            TransactionQuery.Page = 1
      }

      { state with TransactionQuery = query }, Cmd.none
   | LoadTransactions(query, Started) ->
      let load = async {
         let! res = TransactionService.getTransactions query
         return LoadTransactions(query, Finished res)
      }

      let page = query.Page
      let deferredPageData = state.Transactions.TryFind page
      let state = { state with TransactionQuery = query }

      match deferredPageData with
      | Some(Deferred.Resolved(Ok _)) when page <> 1 -> state, Cmd.none
      | _ ->
         {
            state with
               Transactions =
                  state.Transactions |> Map.add page Deferred.InProgress
         },
         Cmd.fromAsync load
   | LoadTransactions(query, Finished(Ok txnsOpt)) ->
      let resolvedTxns = Deferred.Resolved(Ok txnsOpt)

      {
         state with
            Transactions =
               state.Transactions
               |> Map.change query.Page (Option.map (fun _ -> resolvedTxns))
      },
      Cmd.none
   | LoadTransactions(query, Finished(Error err)) ->
      Log.error
         $"Error loading transactions for {query.AccountId} - Page {query.Page}: {err}"

      state, Cmd.none
   | TransactionPropsResolved txnsDeferred ->
      {
         state with
            Transactions = Map [ 1, txnsDeferred ]
      },
      Cmd.none
   | ViewTransaction(txnId) ->
      let queryString =
         {
            Routes.IndexUrl.accountBrowserQuery () with
               Action = None
               Transaction = Some txnId
         }
         |> AccountBrowserQuery.toQueryParams
         |> Router.encodeQueryString

      state,
      Cmd.navigate (
         "account",
         string state.TransactionQuery.AccountId,
         queryString
      )

let renderControlPanel
   state
   dispatch
   (categories: Map<int, TransactionCategory>)
   =
   let query = state.TransactionQuery
   let page = query.Page
   let currPageData = state.Transactions.TryFind page
   let nextPageData = state.Transactions.TryFind(page + 1)

   let endOfPagination =
      match currPageData, nextPageData with
      | _, (Some(Deferred.Resolved(Ok None))) -> true
      | (Some(Deferred.Resolved(Ok None))), _ -> true
      | _, _ -> false

   let onDatePillSelected () =
      dispatch <| Msg.SelectFilterView TransactionFilterView.Date

   let onDatePillDeleted () =
      dispatch <| Msg.UpdateFilter(TransactionFilter.Date None)

   let onCategoryPillSelected () =
      dispatch <| Msg.SelectFilterView TransactionFilterView.Category

   let onCategoryPillDeleted () =
      dispatch <| Msg.UpdateFilter(TransactionFilter.Category None)

   let onAmountPillSelected () =
      dispatch <| Msg.SelectFilterView TransactionFilterView.Amount

   let onAmountPillDeleted () =
      dispatch <| Msg.UpdateFilter(TransactionFilter.Amount None)

   let onMoneyFlowPillDeleted () =
      dispatch <| Msg.UpdateFilter(TransactionFilter.MoneyFlow None)

   classyNode Html.div [ "transaction-table-control-panel" ] [
      Html.button [
         attr.children [ Fa.i [ Fa.Solid.Filter ] []; Html.span "Filter" ]

         attr.onClick (fun e ->
            e.preventDefault ()
            dispatch Msg.ToggleFilterView)
      ]

      match query.DateRange with
      | None -> ()
      | Some(dateStart, dateEnd) ->
         let dateStart, dateEnd = DateTime.formatRangeShort dateStart dateEnd

         FilterPill.render
            $"{dateStart} - {dateEnd}"
            onDatePillSelected
            onDatePillDeleted

      match query.Category with
      | None -> ()
      | Some(CategoryFilter.IsCategorized isCat) ->
         FilterPill.render
            (if isCat then "Categorized" else "Uncategorized")
            onCategoryPillSelected
            onCategoryPillDeleted
      | Some(CategoryFilter.CategoryIds catIds) ->
         let size = catIds.Length

         let content =
            if size > 1 then
               Some $"Categories ({size})"
            else
               categories |> Map.tryFind catIds.Head |> Option.map _.Name

         match content with
         | None -> ()
         | Some content ->
            FilterPill.render
               content
               onCategoryPillSelected
               onCategoryPillDeleted

      match query.MoneyFlow with
      | Some MoneyFlow.In ->
         FilterPill.render
            "Money in"
            onAmountPillSelected
            onMoneyFlowPillDeleted
      | Some MoneyFlow.Out ->
         FilterPill.render
            "Money out"
            onAmountPillSelected
            onMoneyFlowPillDeleted
      | _ -> ()

      match query.Amount with
      | None -> ()
      | Some(AmountFilter.GreaterThanOrEqualTo amount) ->
         FilterPill.render
            $"≥ ${amount}"
            onAmountPillSelected
            onAmountPillDeleted
      | Some(AmountFilter.LessThanOrEqualTo amount) ->
         FilterPill.render
            $"≤ ${amount}"
            onAmountPillSelected
            onAmountPillDeleted
      | Some(AmountFilter.Between(amountStart, amountEnd)) ->
         FilterPill.render
            $"${amountStart} - ${amountEnd}"
            onAmountPillSelected
            onAmountPillDeleted

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
               <| Msg.LoadTransactions(
                  { query with Page = query.Page + 1 },
                  Started
               ))
      ]

      Html.a [
         attr.children [ Fa.i [ Fa.Solid.CaretLeft ] [] ]

         attr.href ""

         attr.classes [
            "pagination"
            if page = 1 then
               "secondary"
         ]

         if page = 1 then
            attr.ariaDisabled true

         attr.onClick (fun e ->
            e.preventDefault ()

            if page = 2 then
               dispatch ResetPageIndex
            elif page > 2 then
               dispatch
               <| LoadTransactions(
                  { query with Page = query.Page - 1 },
                  Started
               ))
      ]

      Html.a [
         attr.children [
            Fa.i [
               if state.TransactionQuery.Diagnostic then
                  Fa.Solid.EyeSlash
               else
                  Fa.Solid.Eye
            ] []
         ]

         attr.href ""

         attr.onClick (fun e ->
            e.preventDefault ()
            dispatch ToggleDiagnosticView)

         attr.custom (
            "data-tooltip",
            if state.TransactionQuery.Diagnostic then
               "Hide Diagnostic Events"
            else
               "Show Diagnostic Events"
         )
         attr.custom ("data-placement", "left")
      ]
   ]

let renderTableRow
   (account: Account)
   (evt: AccountEvent)
   (selectedTxnId: EventId option)
   (merchants: Map<string, Merchant>)
   dispatch
   =
   let txn =
      eventWithMerchantAlias evt merchants |> transactionUIFriendly account

   let orDefaultValue opt = opt |> Option.defaultValue "-"
   let _, envelope = AccountEnvelope.unwrap evt

   Html.tr [
      attr.key (string envelope.Id)

      match selectedTxnId with
      | Some txnId when txnId = envelope.Id -> attr.classes [ "selected" ]
      | _ -> ()

      if TransactionDetail.hasRenderImplementation evt then
         attr.onClick (fun _ -> dispatch (Msg.ViewTransaction envelope.Id))
      else
         attr.style [ style.cursor.defaultCursor; style.borderLeftWidth 0 ]

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

let renderTable
   (account: Account)
   (txns: AccountEvent list)
   (selectedTxnId: EventId option)
   (merchants: Map<string, Merchant>)
   dispatch
   =
   Html.table [
      attr.classes [ "clickable-table" ]
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

         Html.tbody [
            for txn in txns ->
               renderTableRow account txn selectedTxnId merchants dispatch
         ]
      ]
   ]

[<ReactComponent>]
let TransactionTableComponent
   (account: Account)
   (deferred: Deferred<AccountAndTransactionsMaybe>)
   (realtimeTxns: AccountEvent list)
   =
   let txnsDeferred = (Deferred.map << Result.map << Option.map) snd deferred
   let categories = React.useContext TransactionCategoryProvider.context
   let merchants = React.useContext MerchantProvider.stateContext
   let browserQuery = Routes.IndexUrl.accountBrowserQuery ()

   let txnQuery =
      TransactionService.transactionQueryFromAccountBrowserQuery
         account.AccountId
         browserQuery

   let state, dispatch =
      React.useElmish (
         init txnsDeferred txnQuery,
         update,
         [| box account.AccountId |]
      )

   let txns = Map.tryFind state.TransactionQuery.Page state.Transactions

   let q = state.TransactionQuery

   let noFilterSelected =
      match q.Amount, q.Category, q.MoneyFlow, q.DateRange, q.Page with
      | None, None, None, None, 1 -> true
      | _ -> false

   React.useEffect (
      (fun _ ->
         let txnsResolvedInState =
            Map.tryFind state.TransactionQuery.Page state.Transactions
            |> Option.map Deferred.resolved

         let txnsResolvedInProps = Deferred.resolved txnsDeferred

         match txnsResolvedInState, txnsResolvedInProps with
         | Some false, true
         | Some true, false ->
            dispatch <| TransactionPropsResolved txnsDeferred
         | _ -> ()),
      [| box deferred |]
   )

   React.fragment [
      Html.progress [
         attr.style [ style.marginBottom 5 ]
         attr.custom ("data-transactions-loader", "")

         match txns with
         | Some(Deferred.Resolved _) -> attr.value 100
         | _ -> ()
      ]

      classyNode Html.figure [ "control-panel-and-transaction-table-container" ] [
         renderControlPanel state dispatch categories

         match state.FilterView with
         | None -> ()
         | Some view ->
            renderTransactionFilters
               browserQuery
               categories
               view
               (Msg.SelectFilterView >> dispatch)
               (Msg.UpdateFilter >> dispatch)
               (fun () -> dispatch Msg.ToggleFilterView)

         match txns with
         | Some(Resolved(Ok None)) -> Html.p "No transactions found."
         | Some(Resolved(Ok(Some txns))) ->
            // Combine txns matching query with txns received in real-time
            // if no filters applied.
            // TODO: Display real-time events separately & without regard to
            //       applied filters.
            let txns =
               if noFilterSelected then
                  realtimeTxns @ txns
                  |> List.distinctBy (AccountEnvelope.unwrap >> snd >> _.Id)
               else
                  txns

            renderTable account txns browserQuery.Transaction merchants dispatch
         | _ -> ()
      ]
   ]
