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
open Contexts

type Url =
   | Account
   | AccountSelected of Guid
   | AccountEdit of Guid
   | AccountActionOpen of Guid * FormView
   | NotFound

module Url =
   let parse =
      function
      // Matches /
      | [] -> Url.Account
      // Matches /{accountId:Guid}
      | [ Route.Guid accountId ] -> Url.AccountSelected accountId
      // Matches /{accountId:Guid}/deposit
      | [ Route.Guid accountId; "deposit" ] ->
         Url.AccountActionOpen(accountId, FormView.Deposit)
      | [ Route.Guid accountId; "debit" ] ->
         Url.AccountActionOpen(accountId, FormView.Debit)
      | [ Route.Guid accountId; "transfer" ] ->
         Url.AccountActionOpen(accountId, FormView.Transfer)
      | [ Route.Guid accountId; "register-recipient" ] ->
         Url.AccountActionOpen(accountId, FormView.RegisterTransferRecipient)
      | [ Route.Guid accountId; "daily-debit-limit" ] ->
         Url.AccountActionOpen(accountId, FormView.DailyDebitLimit)
      | [ Route.Guid accountId; "card-access" ] ->
         Url.AccountActionOpen(accountId, FormView.CardAccess)
      | _ -> Url.NotFound

   let accountIdMaybe (url: Url) : Guid option =
      match url with
      | AccountSelected id
      | AccountEdit id -> Some id
      | AccountActionOpen(id, _) -> Some id
      | _ -> None

   let accountFormMaybe (url: Url) : FormView option =
      match url with
      | AccountActionOpen(_, form) -> Some form
      | _ -> None

type State = {
   CurrentUrl: Url
   Accounts: Deferred<AccountsMaybe>
   CurrentAccountId: Guid option
   SignalRConnection: SignalR.Connection option
   SignalRCurrentAccountId: Guid option
}

let selectedAccount (state: State) : AccountState option =
   Url.accountIdMaybe state.CurrentUrl
   |> Option.bind (findAccount state.Accounts)

type Msg =
   | UrlChanged of Url
   | LoadAccounts of AsyncOperationStatus<AccountsMaybe>
   | AccountRefreshed of AccountMaybe
   | SignalRConnected of SignalR.Connection
   | SignalRDisconnected
   | AddAccountToSignalRConnectionGroup of
      AsyncOperationStatus<Result<Guid, Err>>
   | AccountEventReceivedViaSignalR of AccountEventPersistedConfirmation

let refreshAccount (accountId) = async {
   let! res = AccountService.getAccount accountId
   return AccountRefreshed(res)
}

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

let init (url: Url) (signalRConnection: SignalR.Connection option) () =
   {
      Accounts = Deferred.Idle
      CurrentUrl = url
      CurrentAccountId = None
      SignalRConnection = signalRConnection
      SignalRCurrentAccountId = None
   },
   Cmd.ofMsg (LoadAccounts Started)

let update msg state =
   match msg with
   | SignalRConnected conn ->
      {
         state with
            SignalRConnection = Some conn
      },
      Cmd.fromAsync (addAccountToSignalRConnectionGroup state)
   | SignalRDisconnected -> { state with SignalRConnection = None }, Cmd.none
   | LoadAccounts Started ->
      let loadAccounts = async {
         let! res = AccountService.getAccounts ()
         return LoadAccounts(Finished res)
      }

      {
         state with
            Accounts = Deferred.InProgress
      },
      Cmd.fromAsync loadAccounts
   | LoadAccounts(Finished(Ok(Some accounts))) ->
      let state = {
         state with
            Accounts = Deferred.Resolved(Ok(Some accounts))
      }

      let firstAccount () =
         let selectedId = accounts |> Map.values |> Seq.head |> _.EntityId
         state, Cmd.navigate ("account", string selectedId)

      match Url.accountIdMaybe state.CurrentUrl with
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
            Cmd.none
   | LoadAccounts(Finished(Ok None)) ->
      {
         state with
            Accounts = Deferred.Resolved(Ok None)
      },
      Cmd.none
   | LoadAccounts(Finished(Error err)) ->
      {
         state with
            Accounts = Deferred.Resolved(Error err)
      },
      Cmd.none
   | AccountRefreshed(Ok(Some account)) ->
      {
         state with
            Accounts =
               updateAccount (fun _ -> account) state.Accounts account.EntityId
      },
      Cmd.none
   | AccountRefreshed _ ->
      Log.error "Issue refreshing account"
      state, Cmd.none
   | UrlChanged url ->
      let idFromUrl = Url.accountIdMaybe url
      let previousAccountId = state.CurrentAccountId

      let state = {
         state with
            CurrentUrl = url
            CurrentAccountId = idFromUrl
      }

      match previousAccountId, idFromUrl with
      | Some previousId, Some currentId when currentId <> previousId ->
         state,
         Cmd.batch [
            Cmd.fromAsync (addAccountToSignalRConnectionGroup state)
            Cmd.fromAsync (refreshAccount (currentId))
         ]
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
   | AccountEventReceivedViaSignalR data ->
      {
         state with
            Accounts =
               updateAccount
                  (fun _ -> data.NewState)
                  state.Accounts
                  data.NewState.EntityId
      },
      Cmd.none

let renderAccountActions state =
   let selectedAccountAndPotentialTransferRecipients
      : (AccountState * PotentialInternalTransferRecipients) option =
      Option.map2
         (fun accounts selectedId ->
            Map.tryFind selectedId accounts
            |> Option.map (fun (found: AccountState) ->
               let potentialRecipients =
                  PotentialInternalTransferRecipients.create found accounts

               found, potentialRecipients))
         (accountsFromDeferred state.Accounts)
         (Url.accountIdMaybe state.CurrentUrl)
      |> Option.flatten

   Html.article [
      match selectedAccountAndPotentialTransferRecipients with
      | None -> ()
      | Some(account, potentialTransferRecipients) ->
         AccountActionsComponent
            account
            potentialTransferRecipients
            (Url.accountFormMaybe state.CurrentUrl)
   ]

[<ReactComponent>]
let AccountDashboardComponent (url: Url) =
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
               (AccountEventReceivedViaSignalR >> dispatch)
               conn
      , [| box signalRConnection |]
   )

   Html.div [
      match accountsFromDeferred state.Accounts with
      | None -> Navigation.NavigationComponent None None
      | Some accounts ->
         Navigation.NavigationComponent (Some accounts) state.CurrentAccountId

      ServiceHealth.ServiceHealthComponent()

      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "grid" ] [
            Html.section [
               Html.h5 "Transactions"
               TransactionTable.TransactionTableComponent(selectedAccount state)
            ]


            Html.aside [ Html.h5 "Actions"; renderAccountActions state ]
         ]
      ]

      match selectedAccount state with
      | None -> ()
      | Some account -> AccountSummary.render account
   ]
