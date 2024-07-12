module AccountDashboard

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish

open Bank.Account.Domain
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
   | AccountEventPersisted of AccountEventPersistedConfirmation

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
   | LoadAccountAndTransactions(_, Finished(Ok(Some(account, txns)))) ->
      {
         state with
            CurrentAccountAndTransactions =
               Deferred.Resolved(Ok(Some(account, txns)))
      },
      Cmd.none
   | LoadAccountAndTransactions _ ->
      Log.error "Issue loading account + transactions."
      state, Cmd.none
   | AccountEventPersisted data ->
      let account = data.Account
      let evt = data.EventPersisted

      {
         state with
            CurrentAccountAndTransactions =
               updateAccountAndTransactions
                  (fun (_, txns) -> account, evt :: txns)
                  state
      },
      Cmd.none

[<ReactComponent>]
let AccountDashboardComponent (url: Routes.AccountUrl) (session: UserSession) =
   let state, dispatch = React.useElmish (init, update, [||])

   let accountProfiles =
      (React.useContext OrgAndAccountProfileProvider.context).AccountProfiles

   let accountIdOpt = Routes.AccountUrl.accountIdMaybe url

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
         match accountProfiles, url with
         | Deferred.Resolved(Ok(Some profiles)), Routes.AccountUrl.Account ->
            profiles
            |> Map.values
            |> Seq.head
            |> _.AccountId
            |> Routes.AccountUrl.selectedPath
            |> Router.navigate
         | _ -> ()
      , [| box accountProfiles; box (string url) |]
   )

   SignalRAccountEventProvider.useAccountEventSubscription {
      ComponentName = "AccountDashboard"
      AccountId = accountIdOpt
      OnReceive = Msg.AccountEventPersisted >> dispatch
   }

   let accountOpt = selectedAccount state

   classyNode Html.div [ "account-dashboard" ] [
      match accountProfiles with
      | Deferred.Resolved(Ok(Some accounts)) ->
         AccountSelectionComponent state.CurrentAccountId accounts
         |> Navigation.Portal
      | _ -> ()

      ServiceHealth.ServiceHealthComponent()

      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "grid" ] [
            Html.section [
               Html.h4 "Transactions"
               match accountOpt with
               | None -> Html.progress []
               | Some account ->
                  TransactionTable.TransactionTableComponent
                     account
                     state.CurrentAccountAndTransactions
            ]

            match
               accountProfiles,
               accountOpt,
               Routes.IndexUrl.accountBrowserQuery().Action
            with
            | Deferred.Resolved(Ok(Some profiles)), Some account, Some action ->
               AccountActions.AccountActionsComponent
                  session
                  account
                  profiles
                  action
                  (AccountEventPersisted >> dispatch)
               |> ScreenOverlay.Portal
            | _, Some account, _ ->
               Html.aside [ AccountActionMenu.render account ]
            | _ -> ()

            match Routes.AccountUrl.transactionIdMaybe url with
            | Some txnId ->
               match accountOpt with
               | Some account ->
                  TransactionDetailComponent session account txnId
               | _ -> Html.progress []
               |> ScreenOverlay.Portal
            | None -> ()
         ]
      ]

      match accountOpt with
      | None -> ()
      | Some account -> AccountSummary.render account
   ]
