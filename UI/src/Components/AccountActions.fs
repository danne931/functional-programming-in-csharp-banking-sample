module AccountActions

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish

open Bank.Account.Domain
open UIDomain.Account
open Bank.Account.Forms
open Bank.Employee.Domain
open Bank.Employee.Forms
open Bank.Transfer.Domain
open Lib.SharedTypes
open EmployeeSearch

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
   PendingAction: Envelope option
   DomesticTransferRedirectOnRecipientCreate: AccountId option
}

type Msg =
   | Cancel
   | NetworkAckCommand of Envelope
   | NetworkAckDomesticRecipient of Envelope * recipientId: AccountId
   | AccountEventReceived of CorrelationId
   | CheckForEventConfirmation of Envelope * attemptNumber: int
   | Noop

let init () =
   {
      PendingAction = None
      DomesticTransferRedirectOnRecipientCreate = None
   },
   Cmd.none

// HTTP request returned 200. Command accepted by network.  Wait
// for account actor cluster to successfully process the command into
// an event and send out a confirmation via SignalR.
let networkAck state (envelope: Envelope) =
   let state = {
      state with
         PendingAction = Some envelope
   }

   let delayedMsg = Msg.CheckForEventConfirmation(envelope, 1)

   state, Cmd.fromTimeout 3000 delayedMsg

let update
   (handlePollingConfirmation: AccountEventPersistedConfirmation list -> unit)
   (accountId: AccountId)
   msg
   (state: State)
   =
   let navigation = navigation accountId

   match msg with
   | Cancel -> state, Cmd.navigate (navigation None)
   | NetworkAckCommand envelope -> networkAck state envelope
   | NetworkAckDomesticRecipient(envelope, accountId) ->
      let state, cmd = networkAck state envelope

      {
         state with
            DomesticTransferRedirectOnRecipientCreate = Some accountId
      },
      cmd
   | AccountEventReceived _ when state.PendingAction.IsNone -> state, Cmd.none
   | AccountEventReceived correlationId ->
      match state.PendingAction with
      | Some envelope when envelope.CorrelationId = correlationId ->
         let state = { state with PendingAction = None }

         match
            Routes.IndexUrl.accountBrowserQuery().Action,
            state.DomesticTransferRedirectOnRecipientCreate
         with
         | Some AccountActionView.RegisterTransferRecipient, Some recipientId ->
            let redirectTo =
               (RecipientAccountEnvironment.Domestic, recipientId)
               |> Some
               |> AccountActionView.Transfer
               |> Some

            {
               state with
                  DomesticTransferRedirectOnRecipientCreate = None
            },
            Cmd.navigate (navigation redirectTo)
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
         init,
         update handlePollingConfirmation account.AccountId,
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
         | AccountActionView.Transfer _ -> "Transfer Money"
         | AccountActionView.Debit -> "Debit Purchase"
         | AccountActionView.Deposit -> "Deposit Cash"
      )

      CloseButton.render (fun _ -> dispatch Cancel)

      match view with
      | AccountActionView.Deposit ->
         DepositForm.DepositFormComponent
            session
            account
            (_.Envelope >> Msg.NetworkAckCommand >> dispatch)
      | AccountActionView.RegisterTransferRecipient ->
         RegisterTransferRecipientForm.RegisterTransferRecipientFormComponent
            session
            account
            None
            (fun conf ->
               match conf.PendingEvent with
               | AccountEvent.DomesticTransferRecipient e ->
                  (conf.Envelope, e.Data.Recipient.AccountId)
                  |> Msg.NetworkAckDomesticRecipient
                  |> dispatch
               | _ -> dispatch (Msg.NetworkAckCommand conf.Envelope))
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
         | Some msg ->
            Html.div [ Html.ins msg ]
            Html.br []
         | None -> ()

         RegisterTransferRecipientForm.RegisterTransferRecipientFormComponent
            session
            account
            (Some accountId)
            (_.Envelope >> Msg.NetworkAckCommand >> dispatch)
      | AccountActionView.Transfer qParamsOpt ->
         TransferForm.TransferFormComponent
            session
            account
            accountProfiles
            (_.Envelope >> Msg.NetworkAckCommand >> dispatch)
            qParamsOpt
      | AccountActionView.Debit ->
         EmployeeCardSelectSearchComponent {|
            OrgId = account.OrgId
            MakeChildrenOnSelect =
               Some
               <| fun card employee -> [
                  DebitForm.DebitFormComponent
                     (_.Envelope >> Msg.NetworkAckCommand >> dispatch)
                     account
                     card.CardId
                     employee
               ]
            OnSelect = None
         |}
   ]
