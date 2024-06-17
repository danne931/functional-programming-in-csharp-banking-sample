module AccountActions

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish
open System

open Bank.Account.Domain
open Bank.Account.UIDomain
open Bank.Account.Forms
open Bank.Transfer.Domain
open Lib.SharedTypes
open FormContainer
open EmployeeCardSelectSearch

type private PendingAction = (AccountCommand * CommandProcessingResponse) option

// If user wants to view the transfer form but hasn't already added
// recipients, redirect them to the transfer recipients creation form
// first.  Once they submit a recipient then transition the view to their
// intended transfer form.
let private shouldRedirectToRegisterRecipient (account: Account) selectedView =
   match selectedView with
   | Some AccountActionView.Transfer when account.TransferRecipients.IsEmpty ->
      true
   | _ -> false


// TODO: Try to remove when refactor to just lookup evtId in the
//       transaction read model instead of account read model.
let private findEventCorrespondingToPendingAction
   (realtimeEvents: AccountEvent list)
   (pendingAction: PendingAction)
   : AccountEvent option
   =
   pendingAction
   |> Option.bind (fun (_, action) ->
      realtimeEvents
      |> List.tryFind (fun evt ->
         let _, envelope = AccountEnvelope.unwrap evt
         envelope.CorrelationId = action.CorrelationId))

let private navigate (accountId: AccountId) (view: AccountActionView option) =
   let queryString =
      {
         Routes.IndexUrl.accountBrowserQuery () with
            Action = view
      }
      |> AccountBrowserQuery.toQueryParams
      |> Router.encodeQueryString

   Cmd.navigate (Routes.AccountUrl.BasePath, string accountId, queryString)

type State = {
   Account: Account
   View: AccountActionView option
   PendingAction: PendingAction
}

type Msg =
   | ShowForm of AccountActionView
   | CancelForm
   | NetworkAckCommand of FormCommand * CommandProcessingResponse
   | AccountEventReceived of AccountEvent list
   | CheckForEventConfirmation of CommandProcessingResponse * attemptNumber: int
   | Noop

let init (account: Account) () =
   let selectedView = Routes.IndexUrl.accountBrowserQuery().Action
   let redirect = shouldRedirectToRegisterRecipient account selectedView

   let selected, cmd =
      if redirect then
         let redirectTo = Some AccountActionView.RegisterTransferRecipient
         redirectTo, navigate account.AccountId redirectTo
      else
         selectedView, Cmd.none

   {
      Account = account
      View = selected
      PendingAction = None
   },
   cmd

let closeForm state = { state with View = None }

let update
   (handleConfirmationReceivedViaPolling:
      AccountEventPersistedConfirmation -> unit)
   msg
   state
   =
   let navigate = navigate state.Account.AccountId

   match msg with
   | ShowForm form ->
      let form =
         if shouldRedirectToRegisterRecipient state.Account (Some form) then
            AccountActionView.RegisterTransferRecipient
         else
            form

      let view = Some form

      { state with View = view }, navigate view
   | CancelForm -> closeForm state, navigate None
   | NetworkAckCommand(command, response) ->
      // HTTP request returned 200. Command accepted by network.  Wait
      // for account actor cluster to successfully process the command into
      // an event and send out a confirmation via SignalR.
      match command with
      | FormCommand.Account command ->
         let state = {
            state with
               PendingAction = Some(command, response)
         }

         let delayedMsg = Msg.CheckForEventConfirmation(response, 1)

         state, Cmd.fromTimeout 3000 delayedMsg
      | FormCommand.Employee _ -> closeForm state, navigate None
   | AccountEventReceived _ when state.PendingAction.IsNone -> state, Cmd.none
   | AccountEventReceived realtimeEvents ->
      let found =
         findEventCorrespondingToPendingAction
            realtimeEvents
            state.PendingAction

      match found with
      | None -> state, Cmd.none
      | Some _ ->
         let state = { state with PendingAction = None }

         match state.View with
         | Some AccountActionView.RegisterTransferRecipient ->
            let redirectTo = Some AccountActionView.Transfer

            let state = { state with View = redirectTo }

            state, navigate redirectTo
         | _ -> closeForm state, navigate None
   // Verify the PendingAction was persisted.
   // If a SignalR event doesn't dispatch a Msg.AccountEventReceived within
   // a few seconds of the initial network request to process the command then
   // assume the SignalR message or connection was dropped. Revert to
   // polling for the latest account read model state.
   | CheckForEventConfirmation(commandResponse, attemptNumber) ->
      let checkAgainMsg =
         Msg.CheckForEventConfirmation(commandResponse, attemptNumber + 1)

      if
         (AccountId.fromEntityId commandResponse.EntityId)
         <> state.Account.AccountId
      then
         Log.info
            "A different account was selected. Discard event confirmation check."

         state, Cmd.none
      elif attemptNumber > 10 then
         Log.error
            "Could not confirm event was processed after several attempts."

         state, Cmd.none
      else
         match state.PendingAction with
         | None -> state, Cmd.none
         | Some(_, action) when
            action.CorrelationId <> commandResponse.CorrelationId
            ->
            state, Cmd.none
         | Some(_, _) ->
            // TODO: Refactor to just lookup id in the transaction read model
            //       instead of account read model
            let getReadModel = async {
               let! accountMaybe =
                  AccountService.getAccount state.Account.AccountId

               match accountMaybe with
               | Error e ->
                  Log.error
                     $"Error checking for updated account state. Retry. {e}"

                  return checkAgainMsg
               | Ok accountOpt ->
                  match accountOpt with
                  | None ->
                     Log.error $"No account found. Notify devs."
                     return Msg.Noop
                  | Some account ->
                     let found =
                        findEventCorrespondingToPendingAction
                           account.Events
                           state.PendingAction

                     match found with
                     | None ->
                        do! Async.Sleep 2500
                        return checkAgainMsg
                     | Some correspondingEvtFound ->
                        handleConfirmationReceivedViaPolling {
                           NewState = account
                           EventPersisted = correspondingEvtFound
                           Date = DateTime.UtcNow
                        }

                        return
                           Msg.AccountEventReceived [ correspondingEvtFound ]
            }

            state, Cmd.fromAsync getReadModel
   | Noop -> state, Cmd.none

let private renderMenuButton dispatch (form: AccountActionView) =
   Html.button [
      attr.classes [ "outline" ]
      attr.onClick (fun _ -> ShowForm form |> dispatch)
      attr.text (
         match form with
         | AccountActionView.RegisterTransferRecipient ->
            "Add a Transfer Recipient"
         | AccountActionView.DailyDebitLimit -> "Daily Debit Limit"
         | AccountActionView.CardAccess -> "Lock Debit Card"
         | _ -> string form
      )
   ]

[<ReactComponent>]
let AccountActionsComponent
   (account: Account)
   (realtimeEvents: AccountEvent list)
   (potentialTransferRecipients: PotentialInternalTransferRecipients)
   (handleConfirmationReceivedViaPolling:
      AccountEventPersistedConfirmation -> unit)
   =
   let state, dispatch =
      React.useElmish (
         init account,
         update handleConfirmationReceivedViaPolling,
         [| box account.AccountId |]
      )

   // TODO: See if I can remove and observe a Option<PendingEvent> on SignalR context
   React.useEffect (
      (fun () ->
         if account.AccountId = state.Account.AccountId then
            dispatch <| AccountEventReceived realtimeEvents),
      [| box realtimeEvents.Length |]
   )

   let renderMenuButton = renderMenuButton dispatch

   match state.View with
   | None ->
      Html.div [
         classyNode Html.div [ "grid" ] [
            renderMenuButton AccountActionView.Debit
            renderMenuButton AccountActionView.Deposit
         ]

         classyNode Html.div [ "grid" ] [
            renderMenuButton AccountActionView.Transfer
         ]

         classyNode Html.div [ "grid" ] [
            renderMenuButton AccountActionView.RegisterTransferRecipient
         ]

         classyNode Html.div [ "grid" ] [
            renderMenuButton AccountActionView.DailyDebitLimit
         ]

         classyNode Html.div [ "grid" ] [
            renderMenuButton AccountActionView.CardAccess
         ]
      ]
   | Some form ->
      classyNode Html.div [ "form-wrapper" ] [
         Html.h6 (
            match form with
            | AccountActionView.RegisterTransferRecipient ->
               "Add a Transfer Recipient"
            | AccountActionView.EditTransferRecipient _ ->
               "Edit Transfer Recipient"
            | AccountActionView.Transfer -> "Transfer Money"
            | AccountActionView.Debit -> "Debit Purchase"
            | AccountActionView.Deposit -> "Deposit Cash"
            | AccountActionView.DailyDebitLimit -> "Set a Daily Allowance"
            | AccountActionView.CardAccess -> "Card Access"
         )

         CloseButton.render (fun _ -> dispatch CancelForm)

         match form with
         | AccountActionView.Deposit ->
            DepositForm.DepositFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
         | AccountActionView.RegisterTransferRecipient ->
            RegisterTransferRecipientForm.RegisterTransferRecipientFormComponent
               state.Account
               potentialTransferRecipients
               None
               (Msg.NetworkAckCommand >> dispatch)
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
               state.Account
               potentialTransferRecipients
               (Some accountId)
               (Msg.NetworkAckCommand >> dispatch)
         | AccountActionView.Transfer ->
            TransferForm.TransferFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
         | AccountActionView.DailyDebitLimit ->
            EmployeeCardSelectSearchComponent
               state.Account.OrgId
               (fun card employee -> [
                  DailyDebitLimitForm.DailyDebitLimitFormComponent
                     (Msg.NetworkAckCommand >> dispatch)
                     card.CardId
                     employee
               ])
         | AccountActionView.Debit ->
            EmployeeCardSelectSearchComponent
               state.Account.OrgId
               (fun card employee -> [
                  DebitForm.DebitFormComponent
                     (Msg.NetworkAckCommand >> dispatch)
                     state.Account
                     card.CardId
                     employee
               ])
      // TODO: move to employee card list page
      (*
         | AccountActionView.CardAccess ->
            CardAccess.CardAccessFormComponent
               (Msg.NetworkAckCommand >> dispatch)
         *)
      ]
