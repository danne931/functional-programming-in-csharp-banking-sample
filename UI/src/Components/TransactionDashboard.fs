module TransactionDashboard

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish

open Bank.Account.Domain
open Bank.Employee.Domain
open UIDomain.Account
open Lib.SharedTypes
open TransactionDetail
open AccountSelection

type State = {
   CurrentAccountId: AccountId option
   CurrentAccountAndTransactions: Deferred<AccountAndTransactionsMaybe>
}

let selectedAccount (state: State) : Account option =
   match state.CurrentAccountAndTransactions with
   | Resolved(Ok(Some(account, _))) -> Some account
   | _ -> None

let updateAccountAndTransactions
   (transform: Account * AccountEvent list -> Account * AccountEvent list)
   (state: State)
   : Deferred<AccountAndTransactionsMaybe>
   =
   (Deferred.map << Result.map << Option.map)
      transform
      state.CurrentAccountAndTransactions

type Msg =
   | LoadAccountAndTransactions of
      AccountId *
      AsyncOperationStatus<AccountAndTransactionsMaybe>
   | AccountEventPersisted of AccountEventPersistedConfirmation list

let init () =
   {
      CurrentAccountId = None
      CurrentAccountAndTransactions = Deferred.Idle
   },
   Cmd.none

let update msg state =
   match msg with
   | LoadAccountAndTransactions(accountId, Started) ->
      let query =
         TransactionService.transactionQueryFromAccountBrowserQuery
            accountId
            (Routes.IndexUrl.accountBrowserQuery ())

      let load = async {
         let! res = AccountService.getAccountAndTransactions query
         return Msg.LoadAccountAndTransactions(accountId, Finished res)
      }

      {
         state with
            CurrentAccountId = Some accountId
            CurrentAccountAndTransactions = Deferred.InProgress
      },
      Cmd.fromAsync load
   | LoadAccountAndTransactions(_, Finished(Ok res)) ->
      {
         state with
            CurrentAccountAndTransactions = Deferred.Resolved(Ok res)
      },
      Cmd.none
   | LoadAccountAndTransactions(_, Finished(Error err)) ->
      Log.error $"Issue loading account + transactions. {err}"
      state, Cmd.none
   | AccountEventPersisted items ->
      if items.IsEmpty then
         Log.error "AccountEventPersisted with no items."
         state, Cmd.none
      else
         let account = items |> List.head |> _.Account
         let evts = items |> List.map _.EventPersisted

         {
            state with
               CurrentAccountAndTransactions =
                  updateAccountAndTransactions
                     (fun (_, txns) -> account, evts @ txns)
                     state
         },
         Cmd.none

[<ReactComponent>]
let TransactionDashboardComponent
   (url: Routes.TransactionUrl)
   (session: UserSession)
   =
   let state, dispatch = React.useElmish (init, update, [||])

   let orgCtx = React.useContext OrgProvider.context
   let orgDispatch = React.useContext OrgProvider.dispatchContext

   let accountIdOpt = Routes.TransactionUrl.accountIdMaybe url

   React.useEffect (
      fun () ->
         match accountIdOpt with
         | Some id -> dispatch <| Msg.LoadAccountAndTransactions(id, Started)
         | _ -> ()
      , [| box (string accountIdOpt) |]
   )

   // Redirect /account to /account/{first-account-id}
   React.useEffect (
      fun () ->
         match orgCtx, url with
         | Deferred.Resolved(Ok(Some org)), Routes.TransactionUrl.Account ->
            org.AccountProfiles
            |> Map.values
            |> Seq.head
            |> _.AccountId
            |> Routes.TransactionUrl.selectedPath
            |> Router.navigate
         | _ -> ()
      , [| box orgCtx; box (string url) |]
   )

   SignalRAccountEventProvider.useAccountEventSubscription {
      ComponentName = "TransactionDashboard"
      AccountId = accountIdOpt
      OnReceive =
         fun conf ->
            let moneyFlow =
               transactionUIFriendly conf.Account conf.EventPersisted
               |> _.MoneyFlow

            if moneyFlow.IsSome then
               orgDispatch
               <| OrgProvider.Msg.BalanceUpdated {|
                  AccountId = conf.Account.AccountId
                  Balance = conf.Account.Balance
                  PersistedEvent = conf.EventPersisted
               |}

            dispatch <| Msg.AccountEventPersisted [ conf ]
   }

   let accountOpt = selectedAccount state

   classyNode Html.div [ "transaction-dashboard" ] [
      match orgCtx with
      | Deferred.Resolved(Ok(Some org)) ->
         AccountSelectionComponent state.CurrentAccountId org.AccountProfiles
         |> Navigation.Portal
      | _ -> ()

      ServiceHealth.ServiceHealthComponent()

      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "grid" ] [
            Html.section [
               Html.h4 "Transactions"
               match state.CurrentAccountAndTransactions with
               | Deferred.Resolved(Error _)
               | Deferred.Resolved(Ok None) -> Html.p "No transactions."
               | Deferred.Resolved(Ok(Some(account, _))) ->
                  TransactionTable.TransactionTableComponent
                     account
                     state.CurrentAccountAndTransactions
               | _ -> Html.progress []
            ]

            match
               orgCtx, accountOpt, Routes.IndexUrl.accountBrowserQuery().Action
            with
            | Deferred.Resolved(Ok(Some org)), Some account, Some action ->
               AccountActions.AccountActionsComponent
                  session
                  account
                  org.AccountProfiles
                  action
                  (AccountEventPersisted >> dispatch)
               |> ScreenOverlay.Portal
            | _, Some account, _ ->
               Html.aside [ AccountActionMenu.render account ]
            | _ -> ()

            match Routes.TransactionUrl.transactionIdMaybe url with
            | Some txnId ->
               match accountOpt with
               | Some account ->
                  TransactionDetailComponent session account txnId
               | _ -> Html.progress []
               |> ScreenOverlay.Portal
            | None -> ()
         ]
      ]

      match orgCtx, accountIdOpt with
      | Deferred.Resolved(Ok(Some org)), Some accountId ->
         match org.AccountProfiles.TryFind accountId with
         | Some profile -> AccountSummary.render profile
         | None -> ()
      | _ -> ()
   ]
