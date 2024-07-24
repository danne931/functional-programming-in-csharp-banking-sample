module EmployeeDashboard

open Feliz
open Feliz.UseElmish
open Elmish
open Feliz.Router
open Fable.FontAwesome

open Bank.Employee.Domain
open UIDomain.Employee
open Lib.SharedTypes
open Lib.NetworkQuery
open EmployeeDetail
open Bank.Employee.Forms.EmployeeCreateForm
open EmployeeSearch
open TableControlPanel

[<RequireQualifiedAccess>]
type EmployeeFilterView =
   | Employees
   | Roles

[<RequireQualifiedAccess>]
type EmployeeFilter =
   | Employees of (SelectedEmployee list) option
   | Roles of (Role list) option

type State = {
   Query: EmployeeQuery
   Employees: Deferred<EmployeesMaybe>
}

let private actionNav (action: EmployeeActionView option) =
   let queryString =
      {
         Routes.IndexUrl.employeeBrowserQuery () with
            Action = action
      }
      |> EmployeeBrowserQuery.toQueryParams
      |> Router.encodeQueryString

   Router.navigate [| Routes.EmployeeUrl.BasePath; queryString |]

type Msg =
   | LoadEmployees of EmployeeQuery * AsyncOperationStatus<EmployeesMaybe>
   | UpdateFilter of EmployeeFilter
   | EmployeeCommandProcessing of EmployeeCommandReceipt

let init (browserQuery: EmployeeBrowserQuery) () =
   let query = EmployeeService.networkQueryFromBrowserQuery browserQuery

   {
      Employees = Deferred.Idle
      Query = query
   },
   Cmd.ofMsg (LoadEmployees(query, Started))

let update (session: UserSession) msg state =
   match msg with
   | LoadEmployees(query, Started) ->
      let loadEmployees = async {
         let! res = EmployeeService.getEmployees session.OrgId query
         return LoadEmployees(query, Finished res)
      }

      {
         Query = query
         Employees = Deferred.InProgress
      },
      Cmd.fromAsync loadEmployees
   | LoadEmployees(_, Finished(Ok(Some employees))) ->
      {
         state with
            Employees = Deferred.Resolved(Ok(Some employees))
      },
      Cmd.none
   | LoadEmployees(_, Finished(Ok None)) ->
      {
         state with
            Employees = Deferred.Resolved(Ok None)
      },
      Cmd.none
   | LoadEmployees(_, Finished(Error err)) ->
      {
         state with
            Employees = Deferred.Resolved(Error err)
      },
      Cmd.none
   | UpdateFilter filter ->
      let browserQuery = Routes.IndexUrl.employeeBrowserQuery ()

      let browserQuery =
         match filter with
         | EmployeeFilter.Employees selected -> {
            browserQuery with
               SelectedEmployees = selected
           }
         | EmployeeFilter.Roles roles -> { browserQuery with Roles = roles }

      let browserQueryParams =
         browserQuery
         |> EmployeeBrowserQuery.toQueryParams
         |> Router.encodeQueryString

      state, Cmd.navigate (Routes.EmployeeUrl.BasePath, browserQueryParams)
   | EmployeeCommandProcessing receipt ->
      let employee = receipt.PendingState

      {
         state with
            Employees =
               (Deferred.map << Result.map << Option.map)
                  (Map.add employee.EmployeeId employee)
                  state.Employees
      },
      Cmd.none

let selectedEmployee
   (selectedEmployeeId: EmployeeId)
   (state: State)
   : Employee option
   =
   match state.Employees with
   | Deferred.Resolved(Ok(Some employees)) ->
      Map.tryFind selectedEmployeeId employees
   | _ -> None

let renderPendingTableRow
   dispatch
   (employee: Employee)
   (selectedEmployeeId: EmployeeId option)
   =
   Html.tr [
      attr.key (string employee.EmployeeId)

      match selectedEmployeeId with
      | Some id when id = employee.EmployeeId -> attr.classes [ "selected" ]
      | _ -> ()

      attr.onClick (fun _ ->
         employee.EmployeeId
         |> EmployeeActionView.ViewEmployee
         |> Some
         |> actionNav)

      attr.children [
         Html.th [ attr.scope "row" ]

         Html.td employee.Name

         Html.td (string employee.Email)

         Html.td "Restore requested by you today"

         Html.td [
            Html.button [
               attr.text "Cancel"

               attr.onClick (fun e -> e.preventDefault ())
            ]
         ]
      ]
   ]

let renderPendingTable
   dispatch
   (employees: Employee list)
   (selectedEmployeeId: EmployeeId option)
   =
   Html.table [
      attr.classes [ "clickable-table" ]
      attr.role "grid"
      attr.children [
         Html.thead [
            Html.tr [
               Html.th [ attr.scope "col" ]

               Html.th [ attr.scope "col"; attr.text "Name" ]

               Html.th [ attr.scope "col"; attr.text "Email" ]
            ]
         ]

         Html.tbody [
            for employee in employees ->
               renderPendingTableRow dispatch employee selectedEmployeeId
         ]
      ]
   ]

let renderTableRow
   (employee: Employee)
   (selectedEmployeeId: EmployeeId option)
   =
   Html.tr [
      attr.key (string employee.EmployeeId)

      match selectedEmployeeId with
      | Some id when id = employee.EmployeeId -> attr.classes [ "selected" ]
      | _ -> ()

      attr.onClick (fun _ ->
         employee.EmployeeId
         |> EmployeeActionView.ViewEmployee
         |> Some
         |> actionNav)

      attr.children [
         Html.th [ attr.scope "row" ]

         Html.td employee.Name

         Html.td (string employee.Email)

         Html.td employee.Role.Display

         Html.td employee.Status.Display
      ]
   ]

let renderTable
   (employees: Employee list)
   (selectedEmployeeId: EmployeeId option)
   =
   Html.table [
      attr.classes [ "clickable-table" ]
      attr.role "grid"
      attr.children [
         Html.thead [
            Html.tr [
               Html.th [ attr.scope "col" ]

               Html.th [ attr.scope "col"; attr.text "Name" ]

               Html.th [ attr.scope "col"; attr.text "Email" ]

               Html.th [ attr.scope "col"; attr.text "Role" ]

               Html.th [ attr.scope "col"; attr.text "Status" ]
            ]
         ]

         Html.tbody [
            for employee in employees ->
               renderTableRow employee selectedEmployeeId
         ]
      ]
   ]

[<ReactComponent>]
let EmployeeDashboardComponent
   (url: Routes.EmployeeUrl)
   (session: UserSession)
   =
   let browserQuery = Routes.IndexUrl.employeeBrowserQuery ()

   let state, dispatch =
      React.useElmish (
         init browserQuery,
         update session,
         [| box browserQuery.ChangeDetection |]
      )

   let selectedEmployeeId = Routes.EmployeeUrl.employeeIdMaybe url

   let close _ = actionNav None

   classyNode Html.div [ "employee-dashboard" ] [
      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "grid" ] [
            Html.section [
               Html.h4 [ attr.text "Employees" ]

               Html.progress [
                  attr.custom ("data-transactions-loader", "")
                  if Deferred.resolved state.Employees then
                     attr.value 100
               ]

               classyNode Html.figure [ "control-panel-and-table-container" ] [
                  TableControlPanelComponent {|
                     FilterViewOptions = [
                        EmployeeFilterView.Employees, "Employees"
                        EmployeeFilterView.Roles, "Employee Role"
                     ]
                     RenderFilterViewOnSelect =
                        function
                        | EmployeeFilterView.Employees ->
                           EmployeeMultiSelectSearchComponent {|
                              OrgId = session.OrgId
                              OnSelect =
                                 Option.map (fun employees ->
                                    (browserQuery.SelectedEmployees
                                     |> Option.defaultValue [])
                                    @ List.map
                                       (fun (e: Employee) -> {
                                          Id = e.EmployeeId
                                          Name = e.Name
                                          Email = string e.Email
                                       })
                                       employees
                                    |> List.distinctBy _.Email)
                                 >> EmployeeFilter.Employees
                                 >> Msg.UpdateFilter
                                 >> dispatch
                           |}
                        | EmployeeFilterView.Roles ->
                           CheckboxFieldset.render {|
                              Options =
                                 [ Role.Admin; Role.Scholar; Role.CardOnly ]
                                 |> List.map (fun o -> {
                                    Id = o
                                    Display = o.Display
                                 })
                              SelectedItems = browserQuery.Roles
                              OnChange =
                                 EmployeeFilter.Roles
                                 >> Msg.UpdateFilter
                                 >> dispatch
                           |}
                     FilterPills =
                        [
                           {
                              View = EmployeeFilterView.Roles
                              OnDelete =
                                 fun () ->
                                    EmployeeFilter.Roles None
                                    |> Msg.UpdateFilter
                                    |> dispatch
                              Content =
                                 state.Query.Roles
                                 |> Option.map EmployeeQuery.rolesToDisplay
                           }
                        ]
                        @ [
                           match browserQuery.SelectedEmployees with
                           | None -> ()
                           | Some selected ->
                              for employee in selected ->
                                 {
                                    View = EmployeeFilterView.Employees
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
                                             |> EmployeeFilter.Employees
                                             |> Msg.UpdateFilter
                                             |> dispatch
                                    Content = Some employee.Name
                                 }
                        ]
                     SubsequentChildren =
                        Some [
                           Html.button [
                              attr.classes [ "create-employee" ]
                              attr.children [
                                 Fa.i [ Fa.Solid.UserPlus ] []
                                 Html.span "Invite"
                              ]

                              attr.onClick (fun _ ->
                                 actionNav (Some EmployeeActionView.Create))
                           ]
                        ]
                  |}

                  match state.Employees with
                  | Resolved(Error err) ->
                     Html.small "Uh oh. Error getting employees."
                  | Resolved(Ok None) -> Html.small "Uh oh. No employees."
                  | Resolved(Ok(Some employees)) ->
                     let pendingApproval, remaining =
                        employees.Values
                        |> List.ofSeq
                        |> List.partition _.PendingAccessApproval

                     Html.h6 "Pending Approval"

                     if pendingApproval.IsEmpty then
                        Html.small "No pending requests."
                     else
                        renderPendingTable
                           dispatch
                           pendingApproval
                           selectedEmployeeId

                     Html.hr []

                     Html.h6 "Approved"

                     if remaining.IsEmpty then
                        Html.small "Uh oh. No employees."
                     else
                        renderTable remaining selectedEmployeeId
                  | _ -> ()
               ]
            ]

            match url with
            | Routes.EmployeeUrl.EmployeesWithSearchQuery query ->
               match query.Action with
               | Some action ->
                  classyNode Html.article [ "form-wrapper" ] [
                     Html.h6 (
                        match action with
                        | EmployeeActionView.Create -> "Invite Employee"
                        | _ -> "Employee Detail"
                     )

                     CloseButton.render close

                     match action with
                     | EmployeeActionView.Create ->
                        EmployeeCreateFormComponent
                           session
                           (Msg.EmployeeCommandProcessing >> dispatch >> close)
                     | EmployeeActionView.ViewEmployee id ->
                        classyNode Html.article [ "employee-detail" ] [
                           if Deferred.resolved state.Employees then
                              match selectedEmployee id state with
                              | None -> Html.small "Uh oh. Employee not found."
                              | Some employee ->
                                 EmployeeDetailComponent
                                    session
                                    employee
                                    (Msg.EmployeeCommandProcessing >> dispatch)
                           else
                              Html.progress []
                        ]
                  ]
                  |> ScreenOverlay.Portal
               | None -> ()
            | _ -> ()
         ]
      ]
   ]
