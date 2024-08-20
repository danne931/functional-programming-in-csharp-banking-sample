module CardDashboard

open Feliz
open Feliz.UseElmish
open Elmish
open Feliz.Router

open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Employee.Domain
open UIDomain
open UIDomain.Employee
open UIDomain.Card
open Bank.Employee.Forms
open EmployeeSearch
open TableControlPanel
open CardDetail
open Lib.NetworkQuery
open Bank.Employee.Forms.CreateCard

let private actionNav (action: CardActionView option) =
   let queryString =
      {
         Routes.IndexUrl.cardBrowserQuery () with
            Action = action
      }
      |> CardBrowserQuery.toQueryParams
      |> Router.encodeQueryString

   Router.navigate [| Routes.CardUrl.BasePath; queryString |]

[<RequireQualifiedAccess>]
type CardFilterView =
   | Accounts
   | Employees
   | Amount
   | CreatedAt

[<RequireQualifiedAccess>]
type CardFilter =
   | Accounts of (SelectedAccount list) option
   | Employees of (SelectedEmployee list) option
   | Amount of AmountFilter option
   | CreatedAt of DateFilter option

type State = {
   Query: CardQuery
   Cards: Deferred<CardsMaybe>
}

type Msg =
   | LoadCards of CardQuery * AsyncOperationStatus<CardsMaybe>
   | UpdateFilter of CardFilter
   | EmployeeCommandProcessing of EmployeeCommandReceipt

let init (browserQuery: CardBrowserQuery) () =
   let query = EmployeeService.networkQueryFromCardBrowserQuery browserQuery

   { Query = query; Cards = Deferred.Idle },
   Cmd.ofMsg <| LoadCards(query, Started)

let update (session: UserSession) msg state =
   match msg with
   | LoadCards(query, Started) ->
      let load = async {
         let! res = EmployeeService.getCards session.OrgId query
         return LoadCards(query, Finished res)
      }

      {
         Query = query
         Cards = Deferred.InProgress
      },
      Cmd.fromAsync load
   | LoadCards(_, Finished(Ok(Some cards))) ->
      {
         state with
            Cards = Deferred.Resolved(Ok(Some cards))
      },
      Cmd.none
   | LoadCards(_, Finished(Ok None)) ->
      {
         state with
            Cards = Deferred.Resolved(Ok None)
      },
      Cmd.none
   | LoadCards(_, Finished(Error err)) ->
      {
         state with
            Cards = Deferred.Resolved(Error err)
      },
      Cmd.none
   | UpdateFilter filter ->
      let browserQuery = Routes.IndexUrl.cardBrowserQuery ()

      let browserQuery =
         match filter with
         | CardFilter.Accounts selected -> {
            browserQuery with
               SelectedAccounts = selected
           }
         | CardFilter.Employees selected -> {
            browserQuery with
               SelectedEmployees = selected
           }
         | CardFilter.CreatedAt date -> { browserQuery with CreatedAt = date }
         | CardFilter.Amount amount -> { browserQuery with Amount = amount }

      let browserQueryParams =
         browserQuery
         |> CardBrowserQuery.toQueryParams
         |> Router.encodeQueryString

      state, Cmd.navigate (Routes.CardUrl.BasePath, browserQueryParams)
   | EmployeeCommandProcessing receipt ->
      let employee = receipt.PendingState

      {
         state with
            Cards =
               (Deferred.map << Result.map << Option.map)
                  (fun (cards: CardWithMetrics list) ->
                     match receipt.PendingEvent with
                     | EmployeeEvent.CreatedCard e ->
                        {
                           Employee = employee
                           Card = e.Data.Card
                           DailyPurchaseAccrued = 0m
                           MonthlyPurchaseAccrued = 0m
                        }
                        :: cards
                     | _ ->
                        cards
                        |> List.map (fun card ->
                           employee.Cards
                           |> Map.tryFind card.Card.CardId
                           |> Option.map (fun c -> {
                              card with
                                 Card = c
                                 Employee = employee
                           })
                           |> Option.defaultValue card))
                  state.Cards
      },
      Cmd.none

let private close () = actionNav None

let renderTableRow
   (card: CardWithMetrics)
   (accountProfiles: Map<AccountId, AccountProfile>)
   (selectedCardId: CardId option)
   =
   Html.tr [
      attr.key (string card.Card.CardId)

      match selectedCardId with
      | Some id when id = card.Card.CardId -> attr.classes [ "selected" ]
      | _ -> ()

      attr.onClick (fun _ ->
         card.Card.CardId |> CardActionView.CardDetail |> Some |> actionNav)

      attr.children [
         Html.th [ attr.scope "row" ]

         Html.td card.Employee.Name

         Html.td card.Card.Display

         Html.td (Money.format card.MonthlyPurchaseAccrued)

         Html.td (
            card.Card.LastPurchaseAt
            |> Option.map dateUIFriendly
            |> Option.defaultValue "-"
         )

         Html.td (if card.Card.Virtual then "Virtual" else "Physical")

         Html.td (
            accountProfiles.TryFind card.Card.AccountId
            |> Option.map _.Name
            |> Option.defaultValue "-"
         )
      ]
   ]

let renderTable
   (cards: CardWithMetrics list)
   (accounts: Map<AccountId, AccountProfile>)
   (selectedCardId: CardId option)
   =
   Html.table [
      attr.classes [ "clickable-table" ]
      attr.role "grid"
      attr.children [
         Html.thead [
            Html.tr [
               Html.th [ attr.scope "col" ]

               Html.th [ attr.scope "col"; attr.text "Name" ]

               Html.th [ attr.scope "col"; attr.text "Card" ]

               Html.th [ attr.scope "col"; attr.text "Monthly Spending" ]

               Html.th [ attr.scope "col"; attr.text "Last Purchase" ]

               Html.th [ attr.scope "col"; attr.text "Type" ]

               Html.th [ attr.scope "col"; attr.text "Account" ]
            ]
         ]

         Html.tbody [
            for card in cards -> renderTableRow card accounts selectedCardId
         ]
      ]
   ]

[<ReactComponent>]
let CardDashboardComponent (url: Routes.CardUrl) (session: UserSession) =
   let browserQuery = Routes.IndexUrl.cardBrowserQuery ()

   let orgCtx = React.useContext OrgProvider.context

   let state, dispatch =
      React.useElmish (
         init browserQuery,
         update session,
         [| box browserQuery.ChangeDetection |]
      )

   let onSubmit = Msg.EmployeeCommandProcessing >> dispatch >> close

   let selectedCardId =
      match browserQuery.Action with
      | Some(CardActionView.CardDetail cardId) -> Some cardId
      | _ -> None

   classyNode Html.div [ "card-dashboard" ] [
      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "grid" ] [
            Html.section [
               Html.h4 "Cards"

               Html.progress [
                  attr.custom ("data-transactions-loader", "")
                  if Deferred.resolved state.Cards then
                     attr.value 100
               ]

               classyNode Html.figure [ "control-panel-and-table-container" ] [
                  TableControlPanelComponent {|
                     FilterViewOptions = [
                        CardFilterView.Employees, "Employees"
                        CardFilterView.Amount, "Spending"
                        CardFilterView.CreatedAt, "Date Created"
                        CardFilterView.Accounts, "Accounts"
                     ]
                     RenderFilterViewOnSelect =
                        function
                        | CardFilterView.Employees ->
                           EmployeeMultiSelectSearchComponent {|
                              OrgId = session.OrgId
                              Selected = browserQuery.SelectedEmployees
                              OnSelect =
                                 CardFilter.Employees
                                 >> Msg.UpdateFilter
                                 >> dispatch
                              Dependencies = None
                           |}
                        | CardFilterView.Amount ->
                           AmountFilter.AmountFilterComponent
                              browserQuery.Amount
                              (CardFilter.Amount >> Msg.UpdateFilter >> dispatch)
                        | CardFilterView.CreatedAt ->
                           DateFilter.DateFilterComponent
                              browserQuery.CreatedAt
                              (CardFilter.CreatedAt
                               >> Msg.UpdateFilter
                               >> dispatch)
                        | CardFilterView.Accounts ->
                           match orgCtx with
                           | Deferred.Resolved(Ok(Some org)) ->
                              CheckboxFieldset.render {|
                                 Options =
                                    org.AccountProfiles.Values
                                    |> List.ofSeq
                                    |> List.map (fun o -> {
                                       Id = o.AccountId
                                       Display =
                                          $"{o.Name} ({Money.format o.Balance})"
                                    })
                                 SelectedItems =
                                    browserQuery.SelectedAccounts
                                    |> Option.map (List.map _.Id)
                                 OnChange =
                                    Option.map (fun accountIds ->
                                       List.choose
                                          (fun (accountId: AccountId) ->
                                             org.AccountProfiles
                                             |> Map.tryFind accountId
                                             |> Option.map (fun account -> {
                                                Id = accountId
                                                Name = account.Name
                                             }))
                                          accountIds)
                                    >> CardFilter.Accounts
                                    >> Msg.UpdateFilter
                                    >> dispatch
                              |}
                           | Deferred.Resolved(Ok None) ->
                              Html.p "No account profiles."
                           | _ -> Html.progress []
                     FilterPills =
                        [
                           {
                              View = CardFilterView.Accounts
                              OnDelete =
                                 fun () ->
                                    dispatch
                                    <| Msg.UpdateFilter(
                                       CardFilter.Accounts None
                                    )
                              Content =
                                 browserQuery.SelectedAccounts
                                 |> Option.map SelectedAccount.listToDisplay
                           }
                           {
                              View = CardFilterView.CreatedAt
                              OnDelete =
                                 fun () ->
                                    CardFilter.CreatedAt None
                                    |> Msg.UpdateFilter
                                    |> dispatch
                              Content =
                                 state.Query.CreatedAtDateRange
                                 |> Option.map DateFilter.dateRangeDisplay
                           }
                           {
                              View = CardFilterView.Amount
                              OnDelete =
                                 fun () ->
                                    dispatch
                                    <| Msg.UpdateFilter(CardFilter.Amount None)
                              Content =
                                 browserQuery.Amount
                                 |> Option.map AmountFilter.display
                           }
                        ]
                        @ [
                           match browserQuery.SelectedEmployees with
                           | None -> ()
                           | Some selected ->
                              for employee in selected ->
                                 {
                                    View = CardFilterView.Employees
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
                                             |> CardFilter.Employees
                                             |> Msg.UpdateFilter
                                             |> dispatch
                                    Content = Some employee.Name
                                 }
                        ]
                     SubsequentChildren = None
                  |}

                  match state.Cards, orgCtx with
                  | Resolved(Error err), _ ->
                     Html.small "Uh oh. Error getting cards."
                  | Resolved(Ok None), _ -> Html.small "No cards."
                  | Resolved(Ok(Some cards)), Resolved(Ok(Some org)) ->
                     renderTable cards org.AccountProfiles selectedCardId
                  | _ -> ()
               ]
            ]

            Html.aside [ CardActionMenu.render () ]

            match url with
            | Routes.CardUrl.CardsWithSearchQuery query ->
               match query.Action with
               | Some view ->
                  classyNode Html.article [ "form-wrapper" ] [
                     Html.h6 (
                        match view with
                        | CardActionView.CardAccess -> "Manage Card Access"
                        | CardActionView.PurchaseLimit ->
                           "Manage Purchase Limits"
                        | CardActionView.Create -> "Add Employee Card"
                        | CardActionView.CardDetail _ -> "Card Detail"
                     )

                     CloseButton.render (fun _ -> close ())

                     match view with
                     | CardActionView.PurchaseLimit ->
                        EmployeeCardSelectSearchComponent {|
                           OrgId = session.OrgId
                           MakeChildrenOnSelect =
                              Some
                              <| fun card employee -> [
                                 PurchaseLimitForm.PurchaseLimitFormComponent
                                    session
                                    onSubmit
                                    card
                                    employee
                              ]
                           OnSelect = None
                        |}
                     | CardActionView.CardAccess ->
                        EmployeeCardSelectSearchComponent {|
                           OrgId = session.OrgId
                           MakeChildrenOnSelect =
                              Some
                              <| fun card employee -> [
                                 CardAccess.CardAccessFormComponent
                                    session
                                    onSubmit
                                    card
                                    employee
                              ]
                           OnSelect = None
                        |}
                     | CardActionView.Create ->
                        EmployeeSelectSearchComponent {|
                           OrgId = session.OrgId
                           MakeChildrenOnSelect =
                              Some
                              <| fun employee -> [
                                 CreateCardFormComponent onSubmit employee
                              ]
                           OnSelect = None
                        |}
                     | CardActionView.CardDetail cardId ->
                        match state.Cards with
                        | Deferred.Resolved(Ok(Some cards)) ->
                           match
                              cards
                              |> List.tryFind (fun c -> c.Card.CardId = cardId)
                           with
                           | Some card ->
                              CardDetailComponent
                                 session
                                 card
                                 (Msg.EmployeeCommandProcessing >> dispatch)
                           | None -> Html.p $"Card {cardId} not found."
                        | _ -> Html.progress []
                  ]
                  |> ScreenOverlay.Portal
               | _ -> ()
            | _ -> ()
         ]
      ]
   ]
