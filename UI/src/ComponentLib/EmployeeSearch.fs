module EmployeeSearch

open Feliz
open Feliz.UseElmish
open Elmish
open System

open Bank.Employee.Domain
open UIDomain.Employee
open UIDomain.Account
open Lib.SharedTypes

type State = {
   SearchInput: string option
   PreviousSearchQuery: string option
   Employees: Deferred<EmployeesMaybe>
}

type Msg =
   | SetSearchInput of string
   | SearchEmployees of
      OrgId *
      searchQuery: string *
      AsyncOperationStatus<EmployeesMaybe>

let init () =
   {
      SearchInput = None
      PreviousSearchQuery = None
      Employees = Deferred.Idle
   },
   Cmd.none

let update msg state =
   match msg with
   | SetSearchInput str ->
      let state =
         if String.IsNullOrWhiteSpace str then
            {
               state with
                  SearchInput = None
                  PreviousSearchQuery = None
                  Employees = Deferred.Idle
            }
         else
            { state with SearchInput = Some str }

      state, Cmd.none
   | SearchEmployees(orgId, searchQuery, Started) ->
      if state.PreviousSearchQuery = Some searchQuery then
         state, Cmd.none
      else
         let cmd =
            async {
               let! res = EmployeeService.searchEmployees orgId searchQuery
               return SearchEmployees(orgId, searchQuery, Finished res)
            }
            |> Cmd.fromAsync

         {
            state with
               Employees = Deferred.InProgress
               PreviousSearchQuery = Some searchQuery
         },
         cmd
   | SearchEmployees(_, _, Finished(Ok res)) ->
      {
         state with
            Employees = Deferred.Resolved(Ok res)
      },
      Cmd.none
   | SearchEmployees(_, _, Finished(Error err)) ->
      Log.error $"Error fetching employees: {err}"

      {
         state with
            Employees = Deferred.Resolved(Error err)
      },
      Alerts.toastCommand err

[<ReactComponent>]
let EmployeeSearchComponent
   (orgId: OrgId)
   (onInputChange: string option -> unit)
   (dependencies: obj array option)
   (makeChildren: string option -> Deferred<EmployeesMaybe> -> ReactElement)
   =
   let deps = dependencies |> Option.defaultValue [||]
   let state, dispatch = React.useElmish (init, update, deps)

   let searchRef = React.useInputRef ()

   // Fetch employees after a debounce.
   React.useEffect (
      fun () ->
         let timer =
            Fable.Core.JS.setTimeout
               (fun () ->
                  match state.PreviousSearchQuery, state.SearchInput with
                  | None, None -> ()
                  | Some previousQuery, Some query when query = previousQuery ->
                     ()
                  | _ -> onInputChange state.SearchInput

                  match state.SearchInput with
                  | None -> ()
                  | Some searchQuery ->
                     dispatch
                     <| Msg.SearchEmployees(orgId, searchQuery, Started))
               800

         React.createDisposable (fun _ -> Fable.Core.JS.clearTimeout timer)
      , [| box state.SearchInput |]
   )

   React.useEffect (
      fun () ->
         match searchRef.current with
         | Some searchInput -> searchInput.focus ()
         | None -> ()
      , deps
   )

   React.fragment [
      Html.input [
         attr.type' "search"
         attr.name $"search-employees"
         attr.placeholder "Search by name or email"
         attr.ariaLabel "Search employees"
         attr.value (state.SearchInput |> Option.defaultValue "")
         attr.onChange (Msg.SetSearchInput >> dispatch)
         attr.ref searchRef
      ]

      makeChildren state.SearchInput state.Employees
   ]

[<ReactComponent>]
let EmployeeSelectSearchComponent
   (props:
      {|
         OrgId: OrgId
         MakeChildrenOnSelect: (Employee -> ReactElement list) option
         OnSelect: (Employee -> unit) option
      |})
   =
   let selected, setSelected = React.useState<Employee option> None
   let selectedEmployeeId = selected |> Option.map _.EmployeeId

   EmployeeSearchComponent
      props.OrgId
      (fun _ -> setSelected None)
      None
      (fun searchInput employees ->
         match employees with
         | Deferred.InProgress ->
            Html.progress [ attr.custom ("data-employee-search-loader", "") ]
         | Deferred.Resolved(Ok None) -> Html.p "No employees found."
         | Deferred.Resolved(Ok(Some employees)) ->
            React.fragment [
               Html.details [
                  attr.classes [ "dropdown" ]
                  if props.MakeChildrenOnSelect.IsNone then
                     attr.isOpen true
                  elif selected.IsNone then
                     attr.isOpen true

                  attr.children [
                     Html.summary "Select an employee."

                     Html.ul [
                        for employeeId, employee in Map.toSeq employees ->
                           Html.li [
                              Html.label [
                                 Html.input [
                                    attr.type' "radio"
                                    attr.name "employee"
                                    attr.value (string employeeId)
                                    attr.isChecked (
                                       Some employeeId = selectedEmployeeId
                                    )
                                    attr.onChange
                                       (fun (_: Browser.Types.Event) ->
                                          props.OnSelect
                                          |> Option.iter (fun cb ->
                                             cb employee)

                                          setSelected (Some employee))
                                 ]
                                 Html.text
                                    $"{employee.Name} - {employee.Email}"
                              ]
                           ]
                     ]
                  ]
               ]

               match selected, props.MakeChildrenOnSelect with
               | Some employee, Some makeChildren ->
                  yield! makeChildren employee
               | _ -> ()
            ]
         | _ -> Html.none)

let EmployeeMultiSelectSearchComponent
   (props:
      {|
         Selected: SelectedEmployee list option
         OrgId: OrgId
         OnSelect: SelectedEmployee list option -> unit
         Dependencies: obj array option
      |})
   =
   let selected = props.Selected

   let selectedEmployeeIds = selected |> Option.map (List.map _.Id)

   let isSelected (employeeId: EmployeeId) =
      match selectedEmployeeIds with
      | None -> false
      | Some ids -> List.exists (fun id -> id = employeeId) ids

   EmployeeSearchComponent
      props.OrgId
      ignore
      props.Dependencies
      (fun searchInput employees ->
         match employees with
         | Deferred.InProgress ->
            Html.progress [ attr.custom ("data-employee-search-loader", "") ]
         | Deferred.Resolved(Ok None) -> Html.p "No employees found."
         | Deferred.Resolved(Ok(Some employees)) ->
            React.fragment [
               Html.details [
                  attr.classes [ "dropdown" ]
                  attr.isOpen true

                  attr.children [
                     Html.summary "Select employees."

                     Html.ul [
                        for employeeId, employee in Map.toSeq employees ->
                           Html.li [
                              Html.label [
                                 Html.input [
                                    attr.type' "checkbox"
                                    attr.name "employee"
                                    attr.value (string employeeId)
                                    attr.isChecked (isSelected employeeId)
                                    attr.onChange
                                       (fun (_: Browser.Types.Event) ->
                                          let selected =
                                             Option.defaultValue [] selected

                                          let selected =
                                             if isSelected employeeId then
                                                selected
                                                |> List.filter (fun em ->
                                                   em.Id <> employeeId)
                                             else
                                                {
                                                   Id = employee.EmployeeId
                                                   Name = employee.Name
                                                   Email =
                                                      string employee.Email
                                                }
                                                :: selected

                                          let selected =
                                             if selected.IsEmpty then
                                                None
                                             else
                                                Some selected

                                          props.OnSelect selected)
                                 ]
                                 Html.text
                                    $"{employee.Name} - {employee.Email}"
                              ]
                           ]
                     ]
                  ]
               ]
            ]
         | _ -> Html.none)

type EmployeeCardPair = Card * Employee

let employeesMappedByCardId
   : Map<EmployeeId, Employee> -> (CardId * EmployeeCardPair) seq option =
   _.Values
   >> Seq.filter (_.Cards.IsEmpty >> not)
   >> Seq.fold
      (fun acc employee ->
         employee.Cards.Values
         |> Seq.map (fun card -> card.CardId, (card, employee))
         |> Seq.append acc)
      []
   >> fun pairs -> if Seq.isEmpty pairs then None else Some pairs

[<ReactComponent>]
let EmployeeCardSelectSearchComponent
   (props:
      {|
         OrgId: OrgId
         MakeChildrenOnSelect: (Card -> Employee -> ReactElement list) option
         OnSelect: (EmployeeCardPair -> unit) option
      |})
   =
   let selected, setSelected = React.useState<EmployeeCardPair option> None
   let selectedCardId = selected |> Option.map (fst >> _.CardId)

   EmployeeSearchComponent
      props.OrgId
      (fun _ -> setSelected None)
      None
      (fun searchInput employees ->
         match employees with
         | Deferred.InProgress ->
            Html.progress [ attr.custom ("data-employee-search-loader", "") ]
         | Deferred.Resolved(Ok None) -> Html.p "No employees found."
         | Deferred.Resolved(Ok(Some employees)) ->
            match employeesMappedByCardId employees with
            | None -> Html.p "No employee cards found."
            | Some employeeCardPairs ->
               React.fragment [
                  Html.details [
                     attr.classes [ "dropdown" ]
                     if props.MakeChildrenOnSelect.IsNone then
                        attr.isOpen true
                     elif selected.IsNone then
                        attr.isOpen true

                     attr.children [
                        Html.summary "Select an employee card."

                        Html.ul [
                           for cardId, (card, employee) in employeeCardPairs ->
                              let cardName =
                                 card.CardNickname |> Option.defaultValue ""

                              Html.li [
                                 Html.label [
                                    Html.input [
                                       attr.type' "radio"
                                       attr.name "employee-card"
                                       attr.isChecked (
                                          Some cardId = selectedCardId
                                       )
                                       attr.value (string cardId)
                                       attr.onChange
                                          (fun (_: Browser.Types.Event) ->
                                             let pair = card, employee

                                             props.OnSelect
                                             |> Option.iter (fun cb ->
                                                cb pair)

                                             setSelected (Some pair))
                                    ]
                                    Html.text
                                       $"{employee.Name} {cardName}**{card.CardNumberLast4}"
                                 ]
                              ]
                        ]
                     ]
                  ]

                  match selected, props.MakeChildrenOnSelect with
                  | Some(card, employee), Some makeChildren ->
                     yield! makeChildren card employee
                  | _ -> ()
               ]
         | _ -> Html.none)

let EmployeeCardMultiSelectSearchComponent
   (props:
      {|
         OrgId: OrgId
         Selected: SelectedCard list option
         OnSelect: SelectedCard list option -> unit
      |})
   =
   let selected = props.Selected
   let selectedCardIds = selected |> Option.map (List.map _.CardId)

   let isSelected (cardId: CardId) =
      match selectedCardIds with
      | None -> false
      | Some ids -> List.exists (fun id -> id = cardId) ids

   EmployeeSearchComponent props.OrgId ignore None (fun _ employees ->
      match employees with
      | Deferred.InProgress ->
         Html.progress [ attr.custom ("data-employee-search-loader", "") ]
      | Deferred.Resolved(Ok None) -> Html.p "No employees found."
      | Deferred.Resolved(Ok(Some employees)) ->
         match employeesMappedByCardId employees with
         | None -> Html.p "No employee cards found."
         | Some employeeCardPairs ->
            React.fragment [
               Html.details [
                  attr.classes [ "dropdown" ]
                  attr.isOpen true

                  attr.children [
                     Html.summary "Select an employee card."

                     Html.ul [
                        for cardId, (card, employee) in employeeCardPairs ->
                           let cardName =
                              card.CardNickname |> Option.defaultValue ""

                           let cardDisplay =
                              $"{employee.Name} {cardName}**{card.CardNumberLast4}"

                           Html.li [
                              Html.label [
                                 Html.input [
                                    attr.type' "checkbox"
                                    attr.name "employee-card"
                                    attr.isChecked (isSelected cardId)
                                    attr.value (string cardId)
                                    attr.onChange
                                       (fun (_: Browser.Types.Event) ->
                                          let selected =
                                             Option.defaultValue [] selected

                                          let selected =
                                             if isSelected cardId then
                                                selected
                                                |> List.filter (fun c ->
                                                   c.CardId <> cardId)
                                             else
                                                {
                                                   CardId = cardId
                                                   Display = cardDisplay
                                                }
                                                :: selected

                                          let selected =
                                             if selected.IsEmpty then
                                                None
                                             else
                                                Some selected

                                          props.OnSelect selected)
                                 ]
                                 Html.text cardDisplay
                              ]
                           ]
                     ]
                  ]
               ]
            ]
      | _ -> Html.none)
