module AccountActions

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish
open System

open Bank.Account.Domain
open UIDomain.Account
open Bank.Account.Forms
open Bank.Employee.Domain
open Bank.Employee.Forms
open Bank.Transfer.Domain
open Lib.SharedTypes
open EmployeeSearch

// If user wants to view the transfer form but hasn't already added
// recipients, redirect them to the transfer recipients creation form
// first.  Once they submit a recipient then transition the view to their
// intended transfer form.
let shouldRedirectToRegisterRecipient (account: Account) selectedView =
   selectedView = AccountActionView.Transfer
   && account.TransferRecipients.IsEmpty

let navigation (accountId: AccountId) (view: AccountActionView option) =
   let queryString =
      {
         Routes.IndexUrl.accountBrowserQuery () with
            Action = view
      }
      |> AccountBrowserQuery.toQueryParams
      |> Router.encodeQueryString

   [| Routes.TransactionUrl.BasePath; string accountId; queryString |]

type State = {
   Account: Account
   PendingAction: Envelope option
}

type Msg =
   | Cancel
   | NetworkAckCommand of Envelope
   | AccountEventReceived of CorrelationId
   | CheckForEventConfirmation of Envelope * attemptNumber: int
   | Noop

let init (account: Account) (view: AccountActionView) () =
   let cmd =
      if shouldRedirectToRegisterRecipient account view then
         let redirectTo = Some AccountActionView.RegisterTransferRecipient
         Cmd.navigate (navigation account.AccountId redirectTo)
      else
         Cmd.none

   {
      Account = account
      PendingAction = None
   },
   cmd

let update
   (handlePollingConfirmation: AccountEventPersistedConfirmation list -> unit)
   msg
   state
   =
   let navigation = navigation state.Account.AccountId

   match msg with
   | Cancel -> state, Cmd.navigate (navigation None)
   | NetworkAckCommand envelope ->
      // HTTP request returned 200. Command accepted by network.  Wait
      // for account actor cluster to successfully process the command into
      // an event and send out a confirmation via SignalR.
      let state = {
         state with
            PendingAction = Some envelope
      }

      let delayedMsg = Msg.CheckForEventConfirmation(envelope, 1)

      state, Cmd.fromTimeout 3000 delayedMsg
   | AccountEventReceived _ when state.PendingAction.IsNone -> state, Cmd.none
   | AccountEventReceived correlationId ->
      match state.PendingAction with
      | Some envelope when envelope.CorrelationId = correlationId ->
         let state = { state with PendingAction = None }

         match Routes.IndexUrl.accountBrowserQuery().Action with
         | Some AccountActionView.RegisterTransferRecipient ->
            let redirectTo = Some AccountActionView.Transfer
            state, Cmd.navigate (navigation redirectTo)
         | _ -> state, Cmd.navigate (navigation None)
      | _ -> state, Cmd.none
   // Verify the PendingAction was persisted.
   // If a SignalR event doesn't dispatch a Msg.AccountEventReceived within
   // a few seconds of the initial network request to process the command then
   // assume the SignalR message or connection was dropped. Revert to
   // polling for the latest account read model state.
   | CheckForEventConfirmation(commandResponse, attemptNumber) ->
      let checkAgainMsg =
         Msg.CheckForEventConfirmation(commandResponse, attemptNumber + 1)

      if attemptNumber > 10 then
         Log.error
            "Could not confirm event was processed after several attempts."

         state, Cmd.none
      else
         match state.PendingAction with
         | Some action when action.CorrelationId = commandResponse.CorrelationId ->
            let getReadModel = async {
               let! confirmationMaybe =
                  TransactionService.getCorrelatedTransactionConfirmations
                     commandResponse.CorrelationId

               match confirmationMaybe with
               | Error e ->
                  Log.error $"Error checking for txn confirmation. {e}"
                  return Msg.Noop
               | Ok None ->
                  do! Async.Sleep 2500
                  return checkAgainMsg
               | Ok(Some conf) ->
                  handlePollingConfirmation conf
                  return Msg.AccountEventReceived action.CorrelationId
            }

            state, Cmd.fromAsync getReadModel
         | _ -> state, Cmd.none
   | Noop -> state, Cmd.none

[<ReactComponent>]
let AccountActionsComponent
   (session: UserSession)
   (account: Account)
   (accountProfiles: Map<AccountId, AccountProfile>)
   (view: AccountActionView)
   (handlePollingConfirmation: AccountEventPersistedConfirmation list -> unit)
   =
   let state, dispatch =
      React.useElmish (
         init account view,
         update handlePollingConfirmation,
         [| box account.AccountId |]
      )

   SignalRAccountEventProvider.useAccountEventSubscription {
      ComponentName = "AccountAction"
      AccountId = Some account.AccountId
      OnReceive =
         _.EventPersisted
         >> AccountEnvelope.unwrap
         >> snd
         >> _.CorrelationId
         >> Msg.AccountEventReceived
         >> dispatch
   }

   classyNode Html.article [ "form-wrapper" ] [
      Html.h6 (
         match view with
         | AccountActionView.RegisterTransferRecipient ->
            "Add a Transfer Recipient"
         | AccountActionView.EditTransferRecipient _ ->
            "Edit Transfer Recipient"
         | AccountActionView.Transfer -> "Transfer Money"
         | AccountActionView.Debit -> "Debit Purchase"
         | AccountActionView.Deposit -> "Deposit Cash"
      )

      CloseButton.render (fun _ -> dispatch Cancel)

      match view with
      | AccountActionView.Deposit ->
         DepositForm.DepositFormComponent
            session
            state.Account
            (_.Envelope >> Msg.NetworkAckCommand >> dispatch)
      | AccountActionView.RegisterTransferRecipient ->
         RegisterTransferRecipientForm.RegisterTransferRecipientFormComponent
            session
            state.Account
            (PotentialInternalTransferRecipients.create account accountProfiles)
            None
            (_.Envelope >> Msg.NetworkAckCommand >> dispatch)
      | AccountActionView.EditTransferRecipient accountId ->
         let invalidAccount =
            TransferDeclinedReason.InvalidAccountInfo
            |> DomesticTransferProgress.Failed

         let count =
            account.FailedDomesticTransfers
            |> Map.filter (fun _ t -> t.Status = invalidAccount)
            |> Map.count

         let msg = "will be retried upon editing recipient info."

         let msg =
            match count with
            | 0 -> None
            | 1 -> Some $"1 failed transfer {msg}"
            | count -> Some $"{count} failed transfers {msg}"

         match msg with
         | Some msg -> Html.div [ Html.ins msg ]
         | None -> ()

         RegisterTransferRecipientForm.RegisterTransferRecipientFormComponent
            session
            state.Account
            (PotentialInternalTransferRecipients.create account accountProfiles)
            (Some accountId)
            (_.Envelope >> Msg.NetworkAckCommand >> dispatch)
      | AccountActionView.Transfer ->
         TransferForm.TransferFormComponent
            session
            state.Account
            accountProfiles
            (_.Envelope >> Msg.NetworkAckCommand >> dispatch)
      | AccountActionView.Debit ->
         EmployeeCardSelectSearchComponent {|
            OrgId = state.Account.OrgId
            MakeChildrenOnSelect =
               Some
               <| fun card employee -> [
                  DebitForm.DebitFormComponent
                     (_.Envelope >> Msg.NetworkAckCommand >> dispatch)
                     state.Account
                     card.CardId
                     employee
               ]
            OnSelect = None
         |}
   ]
