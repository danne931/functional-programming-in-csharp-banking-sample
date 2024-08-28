module EmployeeHistoryDashboard

open Feliz
open Feliz.UseElmish
open Elmish
open Feliz.Router

open Bank.Employee.Domain
open UIDomain
open UIDomain.Employee
open Lib.SharedTypes
open TableControlPanel
open EmployeeSearch

[<RequireQualifiedAccess>]
type EmployeeHistoryFilterView =
   | Date
   | EventType
   | Employee
   | InitiatedBy

[<RequireQualifiedAccess>]
type EmployeeHistoryFilter =
   | Date of DateFilter option
   | EventFilter of (EmployeeEventGroupFilter list) option
   | Employees of (SelectedEmployee list) option
   | InitiatedBy of (SelectedEmployee list) option

type State = {
   Query: EmployeeHistoryQuery
   History: Map<int, Deferred<EmployeeHistoryMaybe>>
}

type Msg =
   | LoadHistory of
      EmployeeHistoryQuery *
      AsyncOperationStatus<EmployeeHistoryMaybe>
   | UpdateFilter of EmployeeHistoryFilter
   | ResetPageIndex

let init (browserQuery: EmployeeHistoryBrowserQuery) () =
   let query = EmployeeService.networkQueryFromHistoryBrowserQuery browserQuery

   {
      Query = query
      History = Map [ 1, Deferred.Idle ]
   },
   Cmd.ofMsg (LoadHistory(query, Started))

let update (session: UserSession) msg state =
   match msg with
   | LoadHistory(query, Started) ->
      let load = async {
         let! res = EmployeeService.getEmployeeHistory session.OrgId query
         return LoadHistory(query, Finished res)
      }

      {
         Query = query
         History = state.History |> Map.add query.Page Deferred.InProgress
      },
      Cmd.fromAsync load
   | LoadHistory(query, Finished(Ok(history))) ->
      let resolvedHistory = Deferred.Resolved(Ok history)

      {
         state with
            History =
               state.History
               |> Map.change query.Page (Option.map (fun _ -> resolvedHistory))
      },
      Cmd.none
   | LoadHistory(query, Finished(Error err)) ->
      Log.error $"Error loading history for Page {query.Page}: {err}"

      {
         state with
            History =
               Map.change
                  query.Page
                  (Option.map (fun _ -> Deferred.Resolved(Error err)))
                  state.History
      },
      Cmd.none
   | UpdateFilter filter ->
      let browserQuery = Routes.IndexUrl.employeeHistoryBrowserQuery ()

      let browserQuery =
         match filter with
         | EmployeeHistoryFilter.Date filter -> {
            browserQuery with
               Date = filter
           }
         | EmployeeHistoryFilter.EventFilter filter -> {
            browserQuery with
               EventType = filter
           }
         | EmployeeHistoryFilter.Employees selected -> {
            browserQuery with
               SelectedEmployees = selected
           }
         | EmployeeHistoryFilter.InitiatedBy selected -> {
            browserQuery with
               SelectedInitiatedBy = selected
           }

      let browserQueryParams =
         browserQuery
         |> EmployeeHistoryBrowserQuery.toQueryParams
         |> Router.encodeQueryString

      state,
      Cmd.navigate (Routes.EmployeeHistoryUrl.BasePath, browserQueryParams)
   | ResetPageIndex ->
      {
         state with
            Query = {
               state.Query with
                  EmployeeHistoryQuery.Page = 1
            }
      },
      Cmd.none

let renderTableRow (evt: EmployeeHistory) =
   let _, env = EmployeeEnvelope.unwrap evt.Event
   let txn = employeeEventUIFriendly evt

   Html.tr [
      attr.key (string env.Id)

      (*
      match selectedEmployeeId with
      | Some id when id = employee.EmployeeId -> attr.classes [ "selected" ]
      | _ -> ()
      *)

      (*
      attr.onClick (fun _ ->
         Router.navigate (Routes.EmployeeUrl.editPath employee.EmployeeId))
      *)

      attr.children [
         Html.th [ attr.scope "row" ]

         Html.td [
            attr.classes [
               match txn.MoneyFlow with
               | None -> ""
               | Some MoneyFlow.In -> "credit"
               | Some MoneyFlow.Out -> "debit"
            ]

            attr.text txn.Amount
         ]

         Html.td (string txn.Info)

         Html.td txn.Initiator

         Html.td txn.Date
      ]
   ]

let renderTable (history: EmployeeHistory list) =
   Html.table [
      attr.classes [ "clickable-table" ]
      attr.role "grid"
      attr.children [
         Html.thead [
            Html.tr [
               Html.th [ attr.scope "col" ]

               Html.th [ attr.scope "col"; attr.text "Amount" ]

               Html.th [ attr.scope "col"; attr.text "Info" ]

               Html.th [ attr.scope "col"; attr.text "Initiated By" ]

               Html.th [ attr.scope "col"; attr.text "Date" ]
            ]
         ]

         Html.tbody [ for evt in history -> renderTableRow evt ]
      ]
   ]

let renderPagination state dispatch =
   Pagination.render {|
      PaginatedResults = state.History
      Page = state.Query.Page
      OnPageChange =
         fun page ->
            dispatch
            <| Msg.LoadHistory({ state.Query with Page = page }, Started)
      OnPageReset = fun () -> dispatch Msg.ResetPageIndex
   |}

[<ReactComponent>]
let EmployeeHistoryDashboardComponent
   (url: Routes.EmployeeHistoryUrl)
   (session: UserSession)
   =
   let browserQuery = Routes.IndexUrl.employeeHistoryBrowserQuery ()

   let state, dispatch =
      React.useElmish (
         init browserQuery,
         update session,
         [| box browserQuery |]
      )

   let history = Map.tryFind state.Query.Page state.History

   classyNode Html.div [ "employee-history-dashboard" ] [
      classyNode Html.main [ "container-fluid" ] [
         Html.section [
            Html.h4 "Employee History"

            Html.progress [
               match history with
               | Some(Deferred.Resolved _) -> attr.value 100
               | _ -> ()
            ]

            classyNode Html.figure [ "control-panel-and-table-container" ] [
               TableControlPanelComponent {|
                  FilterViewOptions = [
                     EmployeeHistoryFilterView.Employee, "Employee"
                     EmployeeHistoryFilterView.InitiatedBy, "Initiated By"
                     EmployeeHistoryFilterView.Date, "Date"
                     EmployeeHistoryFilterView.EventType, "Event Group"
                  ]
                  RenderFilterViewOnSelect =
                     fun view ->
                        match view with
                        | EmployeeHistoryFilterView.Employee ->
                           EmployeeMultiSelectSearchComponent {|
                              OrgId = session.OrgId
                              Selected = browserQuery.SelectedEmployees
                              OnSelect =
                                 EmployeeHistoryFilter.Employees
                                 >> Msg.UpdateFilter
                                 >> dispatch
                              Dependencies = Some [| string view |]
                           |}
                        | EmployeeHistoryFilterView.InitiatedBy ->
                           EmployeeMultiSelectSearchComponent {|
                              OrgId = session.OrgId
                              Selected = browserQuery.SelectedInitiatedBy
                              OnSelect =
                                 EmployeeHistoryFilter.InitiatedBy
                                 >> Msg.UpdateFilter
                                 >> dispatch
                              Dependencies = Some [| string view |]
                           |}
                        | EmployeeHistoryFilterView.Date ->
                           DateFilter.DateFilterComponent
                              browserQuery.Date
                              (EmployeeHistoryFilter.Date
                               >> Msg.UpdateFilter
                               >> dispatch)
                        | EmployeeHistoryFilterView.EventType ->
                           CheckboxFieldset.render {|
                              Options =
                                 [
                                    EmployeeEventGroupFilter.Invitation
                                    EmployeeEventGroupFilter.Purchase
                                    EmployeeEventGroupFilter.CreatedCard
                                    EmployeeEventGroupFilter.UpdatedRole
                                    EmployeeEventGroupFilter.CardFrozenUnfrozen
                                    EmployeeEventGroupFilter.PurchaseLimitUpdated
                                    EmployeeEventGroupFilter.AccessRestored
                                 ]
                                 |> List.map (fun o -> {
                                    Id = o
                                    Display = o.Display
                                 })
                              SelectedItems = browserQuery.EventType
                              OnChange =
                                 EmployeeHistoryFilter.EventFilter
                                 >> Msg.UpdateFilter
                                 >> dispatch
                           |}
                  FilterPills =
                     [
                        {
                           View = EmployeeHistoryFilterView.Date
                           OnDelete =
                              fun () ->
                                 EmployeeHistoryFilter.Date None
                                 |> Msg.UpdateFilter
                                 |> dispatch
                           Content =
                              state.Query.DateRange
                              |> Option.map DateFilter.dateRangeDisplay
                        }
                        {
                           View = EmployeeHistoryFilterView.EventType
                           OnDelete =
                              fun () ->
                                 EmployeeHistoryFilter.EventFilter None
                                 |> Msg.UpdateFilter
                                 |> dispatch
                           Content =
                              state.Query.EventType
                              |> Option.map
                                    EmployeeEventGroupFilter.listToDisplay
                        }
                     ]
                     @ [
                        match browserQuery.SelectedEmployees with
                        | None -> ()
                        | Some selected ->
                           for employee in selected ->
                              {
                                 View = EmployeeHistoryFilterView.Employee
                                 OnDelete =
                                    fun () ->
                                       selected
                                       |> List.filter (fun e ->
                                          e.Id <> employee.Id)
                                       |> fun es ->
                                          (if es.Length = 0 then
                                              None
                                           else
                                              Some es)
                                          |> EmployeeHistoryFilter.Employees
                                          |> Msg.UpdateFilter
                                          |> dispatch
                                 Content = Some employee.Name
                              }

                        match browserQuery.SelectedInitiatedBy with
                        | None -> ()
                        | Some selected ->
                           for employee in selected ->
                              {
                                 View = EmployeeHistoryFilterView.InitiatedBy
                                 OnDelete =
                                    fun () ->
                                       selected
                                       |> List.filter (fun e ->
                                          e.Id <> employee.Id)
                                       |> fun es ->
                                          (if es.Length = 0 then
                                              None
                                           else
                                              Some es)
                                          |> EmployeeHistoryFilter.InitiatedBy
                                          |> Msg.UpdateFilter
                                          |> dispatch
                                 Content = Some $"Initiated By: {employee.Name}"
                              }
                     ]
                  SubsequentChildren = Some [ renderPagination state dispatch ]
               |}

               classyNode Html.div [ "employee-history-table" ] [
                  match history with
                  | Some(Resolved(Error _)) ->
                     Html.small "Uh oh. Error getting employee history."
                  | Some(Resolved(Ok None)) -> Html.small "No employee history."
                  | Some(Resolved(Ok(Some history))) ->
                     if history.IsEmpty then
                        Html.small "No employee history."
                     else
                        renderTable history
                        renderPagination state dispatch
                  | _ -> ()
               ]
            ]
         ]
      ]
   ]
