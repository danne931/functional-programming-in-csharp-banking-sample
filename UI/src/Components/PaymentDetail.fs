module PaymentDetail

open System
open Feliz
open Feliz.UseElmish
open Elmish
open Elmish.SweetAlert

open Bank.Account.Domain
open Bank.Employee.Domain
open Bank.Transfer.Domain
open Bank.Account.Forms.PaymentFulfillment
open UIDomain.Account
open Lib.Time
open Lib.SharedTypes

type State = { IsFulfillingPayment: bool }

[<RequireQualifiedAccess>]
type Confirmation =
   | CancelPayment of Payment
   | DeclinePayment of Payment

type Msg =
   | TogglePaymentFulfillment
   | DismissConfirmation
   | ShowConfirmation of Confirmation
   | ConfirmCancelPaymentRequest of
      reason: string option *
      Payment *
      AsyncOperationStatus<Result<AccountCommandReceipt, Err>>
   | ConfirmDeclinePaymentRequest of
      reason: string option *
      Payment *
      AsyncOperationStatus<Result<AccountCommandReceipt, Err>>

let init () =
   { IsFulfillingPayment = false }, Cmd.none

let getAccount (payment: Payment) =
   payment
   |> Payment.baseInfo
   |> fun p -> p.Payee.AccountId
   |> AccountService.getAccount

let update
   (notifyParentOnUpdate: AccountCommandReceipt -> unit)
   (session: UserSession)
   msg
   state
   =
   match msg with
   | TogglePaymentFulfillment ->
      {
         state with
            IsFulfillingPayment = not state.IsFulfillingPayment
      },
      Cmd.none
   | DismissConfirmation -> state, Cmd.none
   | ShowConfirmation confirmation ->
      let confAction =
         match confirmation with
         | Confirmation.DeclinePayment _ -> "decline"
         | Confirmation.CancelPayment _ -> "cancel"

      let confirm =
         InputAlert(
            function
            | InputAlertResult.Confirmed reason ->
               let reason =
                  if String.IsNullOrWhiteSpace reason then
                     None
                  else
                     Some reason

               match confirmation with
               | Confirmation.DeclinePayment p ->
                  Msg.ConfirmDeclinePaymentRequest(reason, p, Started)
               | Confirmation.CancelPayment p ->
                  Msg.ConfirmCancelPaymentRequest(reason, p, Started)
            | InputAlertResult.Dismissed _ -> Msg.DismissConfirmation
         )
            .Title(
               $"Are you sure you want to {confAction} this payment request?"
            )
            .Type(AlertType.Question)
            .InputType(InputAlertType.TextArea)
            .Placeholder($"Reason to {confAction}")
            .ShowCloseButton(true)

      state, SweetAlert.Run confirm
   | ConfirmCancelPaymentRequest(reason, payment, Started) ->
      let cancel = async {
         let! accountOpt = getAccount payment

         match payment, accountOpt with
         | Payment.ThirdParty _, _ ->
            let err =
               NotImplementedException("third party payment")
               |> Err.NotImplementedError
               |> Error

            return ConfirmCancelPaymentRequest(reason, payment, Finished err)
         | Payment.Platform p, Ok(Some account) ->
            let cmd =
               let initiator = InitiatedById session.EmployeeId

               CancelPlatformPaymentCommand.create initiator {
                  RequestedPayment = {
                     PlatformPaymentRequested.fromPayment p with
                        BaseInfo.InitiatedById = initiator
                  }
                  Reason = reason
               }
               |> AccountCommand.CancelPlatformPayment

            let! res = AccountService.submitCommand account cmd
            return ConfirmCancelPaymentRequest(reason, payment, Finished res)
         | _ ->
            let err = Err.UnexpectedError "Error" |> Error
            return ConfirmCancelPaymentRequest(reason, payment, Finished err)
      }

      state, Cmd.fromAsync cancel
   | ConfirmCancelPaymentRequest(_, p, Finished(Ok receipt)) ->
      notifyParentOnUpdate receipt

      state,
      Alerts.toastSuccessCommand
         $"Cancelled payment request to {Payment.payer p}"
   | ConfirmCancelPaymentRequest(_, _, Finished(Error err)) ->
      Log.error (string err)
      state, Alerts.toastCommand err
   | ConfirmDeclinePaymentRequest(reason, payment, Started) ->
      let decline = async {
         let! accountOpt = getAccount payment

         match payment, accountOpt with
         | Payment.ThirdParty _, _ ->
            let err =
               NotImplementedException("third party payment")
               |> Err.NotImplementedError
               |> Error

            return ConfirmDeclinePaymentRequest(reason, payment, Finished err)
         | Payment.Platform p, Ok(Some account) ->
            let cmd =
               let initiator = InitiatedById session.EmployeeId

               DeclinePlatformPaymentCommand.create initiator {
                  RequestedPayment = {
                     PlatformPaymentRequested.fromPayment p with
                        BaseInfo.InitiatedById = initiator
                  }
                  Reason = reason
               }
               |> AccountCommand.DeclinePlatformPayment

            let! res = AccountService.submitCommand account cmd
            return ConfirmDeclinePaymentRequest(reason, payment, Finished res)
         | _ ->
            let err = Err.UnexpectedError "Error" |> Error
            return ConfirmDeclinePaymentRequest(reason, payment, Finished err)
      }

      state, Cmd.fromAsync decline
   | ConfirmDeclinePaymentRequest(_, p, Finished(Ok receipt)) ->
      notifyParentOnUpdate receipt
      let payee = (Payment.baseInfo p).Payee.OrgName
      state, Alerts.toastSuccessCommand $"Declined payment request from {payee}"
   | ConfirmDeclinePaymentRequest(_, _, Finished(Error err)) ->
      Log.error (string err)
      state, Alerts.toastCommand err

[<ReactComponent>]
let PaymentDetailComponent
   (session: UserSession)
   (payment: Payment)
   (accounts: Map<AccountId, Account>)
   (notifyParentOnUpdate: AccountCommandReceipt -> unit)
   =
   let state, dispatch =
      React.useElmish (init, update notifyParentOnUpdate session, [||])

   let baseInfo = Payment.baseInfo payment
   let isPaymentOutgoing = session.OrgId = baseInfo.Payee.OrgId

   classyNode Html.div [ "payment-detail" ] [
      Html.div [
         Html.h5 [
            attr.style [ style.marginBottom 0 ]
            attr.text (Money.format baseInfo.Amount)
         ]
         Html.div [ Html.small (Payment.statusDisplay payment) ]
      ]

      Html.hr []

      Html.br []

      if isPaymentOutgoing then
         Html.div [
            Html.small "Request sent to:"
            Html.p [
               attr.style [ style.marginLeft 10; style.display.inlineElement ]
               attr.text (Payment.payer payment)
            ]
         ]

         Html.div [
            Html.small "Destination Account:"
            Html.p [
               attr.style [ style.marginLeft 10; style.display.inlineElement ]

               attr.text (
                  accounts
                  |> Map.tryFind baseInfo.Payee.AccountId
                  |> Option.map _.FullName
                  |> Option.defaultValue baseInfo.Payee.OrgName
               )
            ]
         ]

         Html.div [
            Html.small "Memo:"
            Html.p [
               attr.style [ style.marginLeft 10; style.display.inlineElement ]
               attr.text baseInfo.Memo
            ]
         ]
      else
         Html.div [
            Html.small "Request sent from:"
            Html.p [
               attr.style [ style.marginLeft 10; style.display.inlineElement ]
               attr.text baseInfo.Payee.OrgName
            ]
         ]

         Html.div [
            Html.small "Memo:"
            Html.p [
               attr.style [ style.marginLeft 10; style.display.inlineElement ]
               attr.text baseInfo.Memo
            ]
         ]

      Html.br []

      classyNode Html.div [ "grid" ] [
         Html.div [
            Html.small "Created on:"
            Html.p (DateTime.format baseInfo.CreatedAt)
         ]

         Html.div [
            Html.small (
               if
                  baseInfo.Expiration.ToUniversalTime().Date > System.DateTime.UtcNow.Date
               then
                  "Due:"
               else
                  "Expired on:"
            )
            Html.p (DateTime.format baseInfo.Expiration)
         ]
      ]

      if state.IsFulfillingPayment then
         PaymentFulfillmentFormComponent
            session
            accounts
            payment
            (fun receipt ->
               dispatch Msg.TogglePaymentFulfillment
               notifyParentOnUpdate receipt)
      else
         match Payment.canManage payment, isPaymentOutgoing with
         | true, true ->
            Html.button [
               attr.classes [ "outline" ]
               attr.text "Cancel payment request"
               attr.onClick (fun _ ->
                  Confirmation.CancelPayment payment
                  |> Msg.ShowConfirmation
                  |> dispatch)
            ]
         | true, false ->
            classyNode Html.div [ "grid" ] [
               Html.button [
                  attr.classes [ "outline" ]
                  attr.text "Decline to pay"
                  attr.onClick (fun _ ->
                     Confirmation.DeclinePayment payment
                     |> Msg.ShowConfirmation
                     |> dispatch)
               ]

               Html.button [
                  attr.classes [ "outline" ]
                  attr.text "Fulfill payment"
                  attr.onClick (fun _ -> dispatch Msg.TogglePaymentFulfillment)
               ]
            ]
         | _ -> ()
   ]
