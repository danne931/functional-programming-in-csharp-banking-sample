module AccountActions

open Feliz
open Feliz.UseElmish
open Feliz.Router
open Elmish
open System

open Bank.Account.Domain
open Bank.Account.UIDomain
open Lib.SharedTypes
open Bank.Account.Forms

[<RequireQualifiedAccess>]
type FormView =
   | Debit
   | Deposit
   | Transfer
   | RegisterTransferRecipient
   | DailyDebitLimit
   | CardAccess

type private PendingAction =
   (AccountCommand * AccountService.ProcessingEventId) option

let formToPath opt =
   match opt with
   | FormView.Debit -> "debit"
   | FormView.Deposit -> "deposit"
   | FormView.Transfer -> "transfer"
   | FormView.RegisterTransferRecipient -> "register-recipient"
   | FormView.DailyDebitLimit -> "daily-debit-limit"
   | FormView.CardAccess -> "card-access"

// If user wants to view the transfer form but hasn't already added
// recipients, redirect them to the transfer recipients creation form
// first.  Once they submit a recipient then transition the view to their
// intended transfer form.
let private shouldRedirectToRegisterRecipient account selectedFormView =
   match selectedFormView with
   | Some FormView.Transfer when account.TransferRecipients.IsEmpty -> true
   | _ -> false

let private findEventCorrespondingToPendingAction
   (account: AccountState)
   (pendingAction: PendingAction)
   : AccountEvent option
   =
   pendingAction
   |> Option.bind (fun (_, processingEvtId) ->
      account.Events
      |> List.tryFind (fun evt ->
         let _, envelope = AccountEnvelope.unwrap evt
         envelope.Id = processingEvtId))

type State = {
   Account: AccountState
   FormView: FormView option
   PendingAction: PendingAction
}

type Msg =
   | ShowForm of FormView
   | CancelForm
   | NetworkAckCommand of AccountCommand * AccountService.ProcessingEventId
   | AccountEventReceived of AccountState
   | CheckForEventConfirmation of
      AccountService.ProcessingEventId *
      accountId: Guid *
      attemptNumber: int
   | Noop
   | Reset of AccountState * FormView option

let init account selectedFormView () =
   let redirect = shouldRedirectToRegisterRecipient account selectedFormView

   let selected, cmd =
      if redirect then
         let redirectTo = FormView.RegisterTransferRecipient

         Some redirectTo,
         Cmd.navigate (
            "account",
            string account.EntityId,
            formToPath redirectTo
         )
      else
         selectedFormView, Cmd.none

   {
      Account = account
      FormView = selected
      PendingAction = None
   },
   cmd

let closeForm state = { state with FormView = None }

let update
   (handleConfirmationReceivedViaPolling:
      AccountEventPersistedConfirmation -> unit)
   msg
   state
   =
   match msg with
   | ShowForm form ->
      let form =
         if shouldRedirectToRegisterRecipient state.Account (Some form) then
            FormView.RegisterTransferRecipient
         else
            form

      let view = Some form

      { state with FormView = view },
      Cmd.navigate ("account", string state.Account.EntityId, formToPath form)
   | CancelForm ->
      closeForm state, Cmd.navigate ("account", string state.Account.EntityId)
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
      | Some correspondingEvt ->
         let state = { state with PendingAction = None }
         let accountId = state.Account.EntityId

         match state.FormView with
         | Some FormView.RegisterTransferRecipient ->
            let redirectTo = FormView.Transfer

            let state = {
               state with
                  FormView = Some redirectTo
            }

            state,
            Cmd.navigate ("account", string accountId, formToPath redirectTo)
         | _ -> closeForm state, Cmd.navigate ("account", string accountId)
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
   | Reset(account, selectedFormView) ->
      {
         Account = account
         FormView = selectedFormView
         PendingAction = None
      },
      Cmd.none

let private renderMenuButton dispatch (form: FormView) =
   Html.button [
      attr.classes [ "outline" ]
      attr.onClick (fun _ -> ShowForm form |> dispatch)
      attr.text (
         match form with
         | FormView.RegisterTransferRecipient -> "Add a Transfer Recipient"
         | FormView.DailyDebitLimit -> "Daily Debit Limit"
         | FormView.CardAccess -> "Lock Debit Card"
         | _ -> string form
      )
   ]

[<ReactComponent>]
let AccountActionsComponent
   (account: AccountState)
   (potentialTransferRecipients: PotentialInternalTransferRecipients)
   (form: FormView option)
   (handleConfirmationReceivedViaPolling:
      AccountEventPersistedConfirmation -> unit)
   =
   let state, dispatch =
      React.useElmish (
         init account form,
         update handleConfirmationReceivedViaPolling,
         [||]
      )

   React.useEffect (
      (fun () -> dispatch <| Reset(account, form)),
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

   match state.FormView with
   | None ->
      Html.div [
         classyNode Html.div [ "grid" ] [
            renderMenuButton FormView.Debit
            renderMenuButton FormView.Deposit
         ]

         classyNode Html.div [ "grid" ] [ renderMenuButton FormView.Transfer ]

         classyNode Html.div [ "grid" ] [
            renderMenuButton FormView.RegisterTransferRecipient
         ]

         classyNode Html.div [ "grid" ] [
            renderMenuButton FormView.DailyDebitLimit
         ]

         classyNode Html.div [ "grid" ] [ renderMenuButton FormView.CardAccess ]
      ]
   | Some form ->
      classyNode Html.div [ "form-wrapper" ] [
         Html.h6 (
            match form with
            | FormView.RegisterTransferRecipient -> "Add a Transfer Recipient"
            | FormView.Transfer -> "Transfer Money"
            | FormView.Debit -> "Debit Purchase"
            | FormView.Deposit -> "Deposit Cash"
            | FormView.DailyDebitLimit -> "Set a Daily Allowance"
            | FormView.CardAccess -> "Card Access"
         )

         CloseButton.render (fun _ -> dispatch CancelForm)

         match form with
         | FormView.RegisterTransferRecipient ->
            RegisterTransferRecipientForm.RegisterTransferRecipientFormComponent
               state.Account
               potentialTransferRecipients
               (Msg.NetworkAckCommand >> dispatch)
         | FormView.Transfer ->
            TransferForm.TransferFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
         | FormView.DailyDebitLimit ->
            DailyDebitLimitForm.DailyDebitLimitFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
         | FormView.Deposit ->
            DepositForm.DepositFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
         | FormView.Debit ->
            DebitForm.DebitFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
         | FormView.CardAccess ->
            CardAccess.CardAccessFormComponent
               state.Account
               (Msg.NetworkAckCommand >> dispatch)
      ]
