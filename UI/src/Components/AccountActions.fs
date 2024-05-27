module AccountActions

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish
open System

open Bank.Account.Domain
open Bank.Account.UIDomain
open Bank.Account.Forms

type private PendingAction =
   (AccountCommand * AccountService.ProcessingEventId) option

// If user wants to view the transfer form but hasn't already added
// recipients, redirect them to the transfer recipients creation form
// first.  Once they submit a recipient then transition the view to their
// intended transfer form.
let private shouldRedirectToRegisterRecipient account selectedView =
   match selectedView with
   | Some AccountActionView.Transfer when account.TransferRecipients.IsEmpty ->
      true
   | _ -> false

let private findEventCorrespondingToPendingAction
   (account: Account)
   (pendingAction: PendingAction)
   : AccountEvent option
   =
   pendingAction
   |> Option.bind (fun (_, processingEvtId) ->
      account.Events
      |> List.tryFind (fun evt ->
         let _, envelope = AccountEnvelope.unwrap evt
         envelope.Id = processingEvtId))


let private navigate (accountId: Guid) (view: AccountActionView option) =
   let queryString =
      {
         Routes.IndexUrl.accountBrowserQuery () with
            Action = view
      }
      |> AccountBrowserQuery.toQueryParams
      |> Router.encodeQueryString

   Cmd.navigate ("account", string accountId, queryString)

type State = {
   Account: Account
   View: AccountActionView option
   PendingAction: PendingAction
}

type Msg =
   | ShowForm of AccountActionView
   | CancelForm
   | NetworkAckCommand of AccountCommand * AccountService.ProcessingEventId
   | AccountEventReceived of Account
   | CheckForEventConfirmation of
      AccountService.ProcessingEventId *
      accountId: Guid *
      attemptNumber: int
   | Noop

let init account () =
   let selectedView = Routes.IndexUrl.accountBrowserQuery().Action
   let redirect = shouldRedirectToRegisterRecipient account selectedView

   let selected, cmd =
      if redirect then
         let redirectTo = Some AccountActionView.RegisterTransferRecipient
         redirectTo, navigate account.EntityId redirectTo
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
   let navigate = navigate state.Account.EntityId

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
   | NetworkAckCommand(command, evtId) ->
      // HTTP request returned 200. Command accepted by network.  Wait
      // for account actor cluster to successfully process the command into
      // an event and send out a confirmation via SignalR.
      let state = {
         state with
            PendingAction = Some(command, evtId)
      }

      let delayedMsg =
         Msg.CheckForEventConfirmation(evtId, state.Account.EntityId, 1)

      state, Cmd.fromTimeout 3000 delayedMsg
   | AccountEventReceived updatedAccount when state.PendingAction.IsNone ->
      { state with Account = updatedAccount }, Cmd.none
   | AccountEventReceived(updatedAccount) ->
      let state = { state with Account = updatedAccount }

      let found =
         findEventCorrespondingToPendingAction state.Account state.PendingAction

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
   | CheckForEventConfirmation(processingEvtId, accountId, attemptNumber) ->
      let checkAgainMsg =
         Msg.CheckForEventConfirmation(
            processingEvtId,
            accountId,
            attemptNumber + 1
         )

      if accountId <> state.Account.EntityId then
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
         | Some(_, evtId) when evtId <> processingEvtId -> state, Cmd.none
         | Some(command, evtId) ->
            let getReadModel = async {
               let! accountMaybe = AccountService.getAccount accountId

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
                           account
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

                        return Msg.AccountEventReceived account
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
   (potentialTransferRecipients: PotentialInternalTransferRecipients)
   (handleConfirmationReceivedViaPolling:
      AccountEventPersistedConfirmation -> unit)
   =
   let state, dispatch =
      React.useElmish (
         init account,
         update handleConfirmationReceivedViaPolling,
         [| box account.EntityId |]
      )

   React.useEffect (
      (fun () ->
         if
            account.EntityId = state.Account.EntityId
            && account.Events.Length > state.Account.Events.Length
         then
            dispatch <| AccountEventReceived account),
      [| box account.Events.Length |]
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
            | AccountActionView.Transfer -> "Transfer Money"
            | AccountActionView.Debit -> "Debit Purchase"
            | AccountActionView.Deposit -> "Deposit Cash"
            | AccountActionView.DailyDebitLimit -> "Set a Daily Allowance"
            | AccountActionView.CardAccess -> "Card Access"
         )

         CloseButton.render (fun _ -> dispatch CancelForm)

         match form with
         | AccountActionView.RegisterTransferRecipient ->
            RegisterTransferRecipientForm.RegisterTransferRecipientFormComponent
               state.Account
               potentialTransferRecipients
               (Msg.NetworkAckCommand >> dispatch)
         | AccountActionView.Transfer ->
            TransferForm.TransferFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
         | AccountActionView.DailyDebitLimit ->
            DailyDebitLimitForm.DailyDebitLimitFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
         | AccountActionView.Deposit ->
            DepositForm.DepositFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
         | AccountActionView.Debit ->
            DebitForm.DebitFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
         | AccountActionView.CardAccess ->
            CardAccess.CardAccessFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
      ]
