module EmployeeCardSelectSearch

open Feliz
open Feliz.UseElmish
open Elmish
open System

open Bank.Employee.Domain
open Bank.Employee.UIDomain
open Lib.SharedTypes
open AsyncUtil

type EmployeeCardPair = Card * Employee

let employeesMappedByCardId: Employee list -> Map<CardId, EmployeeCardPair> =
   List.fold
      (fun acc employee ->
         employee.Cards.Values
         |> List.ofSeq
         |> List.map (fun card -> card.CardId, (card, employee))
         |> List.append acc)
      []
   >> Map.ofList

type State = {
   SelectedPair: EmployeeCardPair option
   SearchInput: string option
   PreviousSearchQuery: string option
   Employees: Deferred<Result<Map<CardId, EmployeeCardPair>, Err>>
}

type Msg =
   | SetSelectedPair of EmployeeCardPair option
   | SetSearchInput of string
   | SearchEmployees of
      OrgId *
      searchQuery: string *
      AsyncOperationStatus<EmployeesMaybe>

let init () =
   {
      SelectedPair = None
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
                  SelectedPair = None
            }
         else
            { state with SearchInput = Some str }

      state, Cmd.none
   | SetSelectedPair selected ->
      { state with SelectedPair = selected }, Cmd.none
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
      let employees =
         res
         |> Option.map employeesMappedByCardId
         |> Option.defaultValue Map.empty

      {
         state with
            Employees = Deferred.Resolved(Ok employees)
      },
      Cmd.none
   | SearchEmployees(_, _, Finished(Error err)) ->
      Log.error $"Error fetching employees: {err}"

      {
         state with
            Employees = Deferred.Resolved(Error err)
      },
      Alerts.toastCommand err

let renderCardsSelect
   (employeeCardPairs: Map<CardId, EmployeeCardPair>)
   (selectedPair: EmployeeCardPair option)
   dispatch
   =
   Html.details [
      attr.classes [ "dropdown" ]
      if selectedPair.IsNone then
         attr.isOpen true

      attr.children [
         Html.summary "Select an employee card."

         Html.ul [
            for KeyValue(cardId, (card, employee)) in employeeCardPairs ->
               let cardName = card.CardNickname |> Option.defaultValue ""

               Html.li [
                  Html.label [
                     Html.input [
                        attr.type' "radio"
                        attr.name "card"
                        attr.value (string cardId)
                        attr.onChange (fun (_: Browser.Types.Event) ->
                           Some(card, employee)
                           |> Msg.SetSelectedPair
                           |> dispatch)
                     ]
                     Html.text
                        $"{employee.Name} {cardName}**{card.SecurityInfo.CardNumber.Last4}"
                  ]
               ]
         ]
      ]
   ]

[<ReactComponent>]
let EmployeeCardSelectSearchComponent
   (orgId: OrgId)
   (makeChildren: Card -> Employee -> ReactElement list)
   =
   let state, dispatch = React.useElmish (init, update, [||])

   let searchRef = React.useInputRef ()

   // Fetch employee-card pairs after a debounce.
   React.useEffect (
      (fun () ->
         match state.SearchInput with
         | Some searchQuery ->
            let timer =
               Fable.Core.JS.setTimeout
                  (fun () ->
                     dispatch
                     <| Msg.SearchEmployees(orgId, searchQuery, Started))
                  1000

            React.createDisposable (fun _ -> Fable.Core.JS.clearTimeout timer)
         | None -> React.createDisposable (fun _ -> ())),
      [| box state.SearchInput |]
   )

   React.useEffectOnce (fun () ->
      match searchRef.current with
      | Some searchInput -> searchInput.focus ()
      | None -> ())

   React.fragment [
      Html.input [
         attr.type' "search"
         attr.name "search-employees"
         attr.placeholder "Search employee cards"
         attr.ariaLabel "Search employee cards"
         attr.value (state.SearchInput |> Option.defaultValue "")
         attr.onChange (Msg.SetSearchInput >> dispatch)
         attr.ref searchRef
      ]

      match state.SearchInput, state.Employees with
      | Some _, Deferred.Resolved(Ok employees) ->
         if employees.IsEmpty then
            Html.p "No employees found."
         else
            renderCardsSelect employees state.SelectedPair dispatch
      | Some _, Deferred.InProgress ->
         Html.progress [ attr.custom ("data-transactions-loader", "") ]
      | _ -> ()

      match state.SelectedPair with
      | None -> ()
      | Some(card, employee) -> yield! makeChildren card employee
   ]
