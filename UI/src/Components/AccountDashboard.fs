module AccountDashboard

open Feliz
open Feliz.UseElmish
open Elmish
open Feliz.Router
open System

open AsyncUtil
open Bank.Account.Domain
open Bank.Account.UIDomain
open Lib.SharedTypes
open AccountActions
open TransactionDetail
open Contexts

type State = {
   CurrentUrl: Routes.AccountUrl
   AccountProfiles: Deferred<AccountProfilesMaybe>
   CurrentAccountId: Guid option
   CurrentAccountAndTransactions: Deferred<AccountAndTransactionsMaybe>
   RealtimeTransactions: AccountEvent list
   SignalRConnection: SignalR.Connection option
   SignalRCurrentAccountId: Guid option
}

let accountProfiles (state: State) : Map<Guid, AccountProfile> option =
   match state.AccountProfiles with
   | Deferred.Resolved(Ok(Some accounts)) -> Some accounts
   | _ -> None

let selectedProfile (state: State) : AccountProfile option =
   Option.map2
      (fun accountId profiles -> Map.tryFind accountId profiles)
      (Routes.AccountUrl.accountIdMaybe state.CurrentUrl)
      (accountProfiles state)
   |> Option.flatten

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
   | UrlChanged of Routes.AccountUrl
   | LoadAccountProfiles of AsyncOperationStatus<AccountProfilesMaybe>
   | LoadAccountAndTransactions of
      Guid *
      AsyncOperationStatus<AccountAndTransactionsMaybe>
   | SignalRConnected of SignalR.Connection
   | SignalRDisconnected
   | AddAccountToSignalRConnectionGroup of
      AsyncOperationStatus<Result<Guid, Err>>
   | AccountEventPersisted of AccountEventPersistedConfirmation

let addAccountToSignalRConnectionGroup state : Async<Msg> =
   let opt =
      Option.map2
         (fun conn accountIdToAdd -> conn, accountIdToAdd)
         state.SignalRConnection
         state.CurrentAccountId

   match opt with
   | Some(conn, accountIdToAdd) -> async {
      let! res =
         AccountService.addAccountToSignalRConnectionGroup
            conn
            state.SignalRCurrentAccountId
            accountIdToAdd

      return Msg.AddAccountToSignalRConnectionGroup(Finished res)
     }
   | None -> async {
      Log.info "Waiting to add account to SignalR connection group..."
      do! Async.Sleep 500
      return Msg.AddAccountToSignalRConnectionGroup Started
     }

let init
   (url: Routes.AccountUrl)
   (signalRConnection: SignalR.Connection option)
   ()
   =
   {
      AccountProfiles = Deferred.Idle
      CurrentUrl = url
      CurrentAccountId = None
      CurrentAccountAndTransactions = Deferred.Idle
      RealtimeTransactions = []
      SignalRConnection = signalRConnection
      SignalRCurrentAccountId = None
   },
   Cmd.ofMsg (LoadAccountProfiles Started)

let update msg state =
   match msg with
   | SignalRConnected conn ->
      {
         state with
            SignalRConnection = Some conn
      },
      Cmd.fromAsync (addAccountToSignalRConnectionGroup state)
   | SignalRDisconnected -> { state with SignalRConnection = None }, Cmd.none
   | LoadAccountProfiles Started ->
      let loadAccountProfiles = async {
         let! res = AccountService.getAccountProfiles ()
         return LoadAccountProfiles(Finished res)
      }

      {
         state with
            AccountProfiles = Deferred.InProgress
      },
      Cmd.fromAsync loadAccountProfiles
   | LoadAccountProfiles(Finished(Ok(Some accounts))) ->
      let state = {
         state with
            AccountProfiles = Deferred.Resolved(Ok(Some accounts))
      }

      let firstAccount () =
         let selectedId = accounts |> Map.values |> Seq.head |> _.EntityId
         state, Cmd.navigate ("account", string selectedId)

      match Routes.AccountUrl.accountIdMaybe state.CurrentUrl with
      | None -> firstAccount ()
      | Some id ->
         let selected = accounts |> Map.tryFind id

         match selected with
         | None -> firstAccount ()
         | Some _ ->
            {
               state with
                  CurrentAccountId = Some id
            },
            Cmd.ofMsg <| Msg.LoadAccountAndTransactions(id, Started)
   | LoadAccountProfiles(Finished(Ok None)) ->
      {
         state with
            AccountProfiles = Deferred.Resolved(Ok None)
      },
      Cmd.none
   | LoadAccountProfiles(Finished(Error err)) ->
      {
         state with
            AccountProfiles = Deferred.Resolved(Error err)
      },
      Cmd.none
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
            CurrentAccountAndTransactions = Deferred.Idle
            RealtimeTransactions = []
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
   | UrlChanged url ->
      let idFromUrl = Routes.AccountUrl.accountIdMaybe url
      let previousAccountId = state.CurrentAccountId

      let state = {
         state with
            CurrentUrl = url
            CurrentAccountId = idFromUrl
      }

      let accountSelectedCmd currentId =
         Cmd.batch [
            Cmd.fromAsync (addAccountToSignalRConnectionGroup state)
            Cmd.ofMsg (Msg.LoadAccountAndTransactions(currentId, Started))
         ]

      match previousAccountId, idFromUrl with
      | None, Some currentId -> state, accountSelectedCmd currentId
      | Some previousId, Some currentId when currentId <> previousId ->
         state, accountSelectedCmd currentId
      | _ -> state, Cmd.none
   | AddAccountToSignalRConnectionGroup Started ->
      state, Cmd.fromAsync (addAccountToSignalRConnectionGroup state)
   | AddAccountToSignalRConnectionGroup(Finished(Error err)) ->
      Log.error $"Error adding account to Signal R connection group {err}"
      state, Cmd.none
   | AddAccountToSignalRConnectionGroup(Finished(Ok accountIdAdded)) ->
      Log.info $"Added account to SignalR connection group: {accountIdAdded}"

      {
         state with
            SignalRCurrentAccountId = Some accountIdAdded
      },
      Cmd.none
   | AccountEventPersisted data ->
      let account = data.NewState
      let evt = data.EventPersisted

      {
         state with
            RealtimeTransactions = evt :: state.RealtimeTransactions
            CurrentAccountAndTransactions =
               updateAccountAndTransactions
                  (fun (_, txns) -> account, evt :: txns)
                  state
      },
      Cmd.none

let renderAccountActions state dispatch =
   let selectedAccountAndPotentialTransferRecipients
      : (Account * PotentialInternalTransferRecipients) option =
      Option.map2
         (fun accountProfiles account ->
            let potentialRecipients =
               PotentialInternalTransferRecipients.create
                  account
                  accountProfiles

            account, potentialRecipients)
         (accountProfiles state)
         (selectedAccount state)

   Html.article [
      match selectedAccountAndPotentialTransferRecipients with
      | None -> ()
      | Some(account, potentialTransferRecipients) ->
         AccountActionsComponent
            account
            potentialTransferRecipients
            (AccountEventPersisted >> dispatch)
   ]

[<ReactComponent>]
let AccountDashboardComponent (url: Routes.AccountUrl) =
   let signalRContext = React.useContext signalRContext
   let signalRConnection = signalRContext.Connection

   let state, dispatch =
      React.useElmish (init url signalRConnection, update, [||])

   React.useEffect (fun () ->
      if url <> state.CurrentUrl then
         dispatch (UrlChanged url))

   React.useEffect (
      fun () ->
         match state.SignalRConnection, signalRConnection with
         | None, None -> Log.info "Waiting for SignalR connection..."
         | Some _, None -> dispatch SignalRDisconnected
         | Some _, Some _ -> ()
         | None, Some conn ->
            dispatch (SignalRConnected conn)

            AccountService.listenForSignalRMessages
               (AccountEventPersisted >> dispatch)
               conn
      , [| box signalRConnection |]
   )

   let accountOpt = selectedAccount state
   let profileOpt = selectedProfile state

   Html.div [
      match accountProfiles state with
      | Some accounts ->
         Navigation.NavigationComponent (Some accounts) state.CurrentAccountId
      | None -> Navigation.NavigationComponent None None

      ServiceHealth.ServiceHealthComponent()

      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "grid" ] [
            Html.section [
               Html.h5 "Transactions"
               match profileOpt with
               | None -> ()
               | Some profile ->
                  TransactionTable.TransactionTableComponent
                     profile
                     state.CurrentAccountAndTransactions
                     state.RealtimeTransactions
            ]

            Html.aside [
               match Routes.AccountUrl.transactionIdMaybe state.CurrentUrl with
               | Some txnId ->
                  Html.h5 "Transaction Detail"

                  match profileOpt with
                  | Some profile -> TransactionDetailComponent profile txnId
                  | _ -> Html.div [ attr.ariaBusy true ]
               | None ->
                  Html.h5 "Actions"
                  renderAccountActions state dispatch
            ]
         ]
      ]

      match accountOpt with
      | None -> ()
      | Some account -> AccountSummary.render account
   ]
