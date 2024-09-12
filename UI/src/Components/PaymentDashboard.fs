module PaymentDashboard

open Feliz
open Feliz.UseElmish
open Elmish
open Feliz.Router
open Fable.FontAwesome
open System

open Bank.Account.Domain
open Bank.Employee.Domain
open Bank.Transfer.Domain
open UIDomain.Account
open Lib.Time
open Lib.SharedTypes
open PaymentDetail
open Bank.Account.Forms.PaymentRequest

type State = { Payments: Deferred<PaymentsMaybe> }

[<RequireQualifiedAccess>]
type PaymentActionView =
   | RequestPayment
   | ViewPayment of PaymentId

let private actionNav (action: PaymentActionView option) =
   let path =
      match action with
      | Some PaymentActionView.RequestPayment ->
         Routes.PaymentUrl.RequestPaymentPath
      | Some(PaymentActionView.ViewPayment payId) ->
         Routes.PaymentUrl.selectedPath payId
      | None -> [| Routes.PaymentUrl.BasePath |]

   Router.navigate path

type Msg =
   | LoadPayments of AsyncOperationStatus<PaymentsMaybe>
   | PaymentCommandProcessing of AccountCommandReceipt

let init () =
   { Payments = Deferred.Idle }, Cmd.ofMsg (LoadPayments Started)

let updatePlatformPaymentStatus
   (targetId: PaymentId)
   (status: PlatformPaymentStatus)
   (payment: PlatformPayment)
   : PlatformPayment
   =
   if payment.BaseInfo.Id = targetId then
      { payment with Status = status }
   else
      payment

let update (session: UserSession) msg state =
   match msg with
   | LoadPayments Started ->
      let loadPayments = async {
         let! res = AccountService.getPayments session.OrgId
         return LoadPayments(Finished res)
      }

      { Payments = Deferred.InProgress }, Cmd.fromAsync loadPayments
   | LoadPayments(Finished(Ok(Some payments))) ->
      {
         state with
            Payments = Deferred.Resolved(Ok(Some payments))
      },
      Cmd.none
   | LoadPayments(Finished(Ok None)) ->
      {
         state with
            Payments = Deferred.Resolved(Ok None)
      },
      Cmd.none
   | LoadPayments(Finished(Error err)) ->
      {
         state with
            Payments = Deferred.Resolved(Error err)
      },
      Cmd.none
   | PaymentCommandProcessing receipt ->
      {
         state with
            Payments =
               (Deferred.map << Result.map << Option.map)
                  (fun paymentSummary ->
                     match receipt.PendingEvent with
                     | AccountEvent.PlatformPaymentRequested p ->
                        let payment =
                           PlatformPaymentRequested.toPayment p
                           |> Payment.Platform

                        {
                           paymentSummary with
                              OutgoingRequests =
                                 payment :: paymentSummary.OutgoingRequests
                        }
                     | AccountEvent.PlatformPaymentCancelled e ->
                        let updateStatus =
                           updatePlatformPaymentStatus
                              e.Data.BaseInfo.Id
                              PlatformPaymentStatus.Cancelled

                        {
                           paymentSummary with
                              OutgoingRequests =
                                 paymentSummary.OutgoingRequests
                                 |> List.map (function
                                    | Payment.Platform p ->
                                       Payment.Platform(updateStatus p)
                                    // NOT yet implemented
                                    | Payment.ThirdParty p ->
                                       Payment.ThirdParty p)
                        }
                     | AccountEvent.PlatformPaymentPaid e ->
                        let updateStatus =
                           updatePlatformPaymentStatus
                              e.Data.BaseInfo.Id
                              PlatformPaymentStatus.Paid

                        {
                           paymentSummary with
                              IncomingRequests =
                                 paymentSummary.IncomingRequests
                                 |> List.map updateStatus
                        }
                     | AccountEvent.PlatformPaymentDeclined e ->
                        let updateStatus =
                           updatePlatformPaymentStatus
                              e.Data.BaseInfo.Id
                              PlatformPaymentStatus.Declined

                        {
                           paymentSummary with
                              IncomingRequests =
                                 paymentSummary.IncomingRequests
                                 |> List.map updateStatus
                        }
                     | _ -> paymentSummary)
                  state.Payments
      },
      Cmd.none

let selectedPayment
   (payments: Deferred<PaymentsMaybe>)
   (selectedId: PaymentId)
   : Payment option
   =
   match payments with
   | Deferred.Resolved(Ok(Some payments)) ->
      payments.IncomingRequests
      |> List.tryFind (fun p -> p.BaseInfo.Id = selectedId)
      |> Option.map Payment.Platform
      |> Option.orElse (
         payments.OutgoingRequests
         |> List.tryFind (fun p -> (Payment.baseInfo p).Id = selectedId)
      )
   | _ -> None

let renderIncomingTableRow
   (payment: PlatformPayment)
   (selectedId: PaymentId option)
   =
   let info = payment.BaseInfo
   let paymentId = info.Id

   Html.tr [
      attr.key (string paymentId)

      match selectedId with
      | Some id when id = paymentId -> attr.classes [ "selected" ]
      | _ -> ()

      attr.onClick (fun _ ->
         paymentId |> PaymentActionView.ViewPayment |> Some |> actionNav)

      attr.children [
         Html.td (DateTime.formatShort info.Expiration)

         Html.td info.Payee.OrgName

         Html.td (Money.format info.Amount)

         Html.td (Payment.statusDisplay (Payment.Platform payment))
      ]
   ]

let renderIncomingTable
   (payments: PlatformPayment list)
   (selectedId: PaymentId option)
   =
   Html.table [
      attr.classes [ "clickable-table" ]
      attr.role "grid"
      attr.children [
         Html.thead [
            Html.tr [
               Html.th [ attr.scope "col"; attr.text "Due" ]

               Html.th [ attr.scope "col"; attr.text "From" ]

               Html.th [ attr.scope "col"; attr.text "Amount" ]

               Html.th [ attr.scope "col"; attr.text "Status" ]
            ]
         ]

         Html.tbody [
            let payments =
               payments
               |> List.sortBy (Payment.Platform >> Payment.displayPriority)

            for payment in payments -> renderIncomingTableRow payment selectedId
         ]
      ]
   ]

let renderTableRow
   (payment: Payment)
   (selectedId: PaymentId option)
   (profilesOpt: Map<AccountId, AccountProfile> option)
   =
   let paymentBaseInfo = Payment.baseInfo payment
   let paymentId = paymentBaseInfo.Id

   Html.tr [
      attr.key (string paymentId)

      match selectedId with
      | Some id when id = paymentId -> attr.classes [ "selected" ]
      | _ -> ()

      attr.onClick (fun _ ->
         paymentId |> PaymentActionView.ViewPayment |> Some |> actionNav)

      attr.children [
         Html.td (DateTime.formatShort paymentBaseInfo.CreatedAt)

         match payment with
         | Payment.Platform p -> Html.td p.Payer.OrgName
         | Payment.ThirdParty p -> Html.td p.Payer.Name

         Html.td (Money.format paymentBaseInfo.Amount)

         Html.td (Payment.statusDisplay payment)

         profilesOpt
         |> Option.bind (Map.tryFind paymentBaseInfo.Payee.AccountId)
         |> Option.map _.FullName
         |> Option.defaultValue ""
         |> Html.td
      ]
   ]

let renderTable
   (payments: Payment list)
   (selectedId: PaymentId option)
   (profilesOpt: Map<AccountId, AccountProfile> option)
   =
   Html.table [
      attr.classes [ "clickable-table" ]
      attr.role "grid"
      attr.children [
         Html.thead [
            Html.tr [
               Html.th [ attr.scope "col"; attr.text "Created on" ]

               Html.th [ attr.scope "col"; attr.text "To" ]

               Html.th [ attr.scope "col"; attr.text "Amount" ]

               Html.th [ attr.scope "col"; attr.text "Status" ]

               Html.th [ attr.scope "col"; attr.text "Account" ]
            ]
         ]

         Html.tbody [
            let payments = payments |> List.sortBy Payment.displayPriority

            for payment in payments ->
               renderTableRow payment selectedId profilesOpt
         ]
      ]
   ]

[<ReactComponent>]
let PaymentDashboardComponent (url: Routes.PaymentUrl) (session: UserSession) =
   let state, dispatch = React.useElmish (init, update session, [||])
   let orgCtx = React.useContext OrgProvider.context

   let accountProfilesOpt =
      match orgCtx with
      | Deferred.Resolved(Ok(Some org)) -> Some org.AccountProfiles
      | _ -> None

   let selectedPaymentId =
      match url with
      | Routes.PaymentUrl.ViewPayment payId -> Some payId
      | _ -> None

   let close _ = actionNav None

   classyNode Html.div [ "payment-dashboard" ] [
      classyNode Html.main [ "container-fluid" ] [
         classyNode Html.div [ "grid" ] [
            Html.section [
               classyNode Html.div [ "title-and-button-container" ] [
                  Html.h4 [ attr.text "Payments" ]
                  Html.button [
                     attr.children [
                        Fa.i [ Fa.Solid.Pray ] []
                        Html.span "Request Payment"
                     ]

                     attr.onClick (fun _ ->
                        actionNav (Some PaymentActionView.RequestPayment))
                  ]
               ]

               Html.progress [
                  attr.custom ("data-transactions-loader", "")
                  if Deferred.resolved state.Payments then
                     attr.value 100
               ]

               classyNode Html.figure [ "control-panel-and-table-container" ] [
                  match state.Payments with
                  | Resolved(Error err) ->
                     Html.small "Uh oh. Error getting payments."
                  | Resolved(Ok None) -> Html.small "No payments."
                  | Resolved(Ok(Some payments)) ->
                     Html.h6 "Incoming Requests"

                     if payments.IncomingRequests.IsEmpty then
                        Html.small "No incoming payment requests."
                     else
                        renderIncomingTable
                           payments.IncomingRequests
                           selectedPaymentId

                     Html.h6 "Outgoing Requests"

                     if payments.OutgoingRequests.IsEmpty then
                        Html.small "No outgoing payment requests."
                     else
                        renderTable
                           payments.OutgoingRequests
                           selectedPaymentId
                           accountProfilesOpt
                  | _ -> ()
               ]
            ]

            match url with
            | Routes.PaymentUrl.RequestPayment ->
               classyNode Html.article [ "form-wrapper" ] [
                  Html.h6 "Payment Request"

                  CloseButton.render close

                  match orgCtx with
                  | Deferred.Resolved(Ok(Some o)) ->
                     PaymentRequestFormComponent
                        session
                        o.Org
                        o.AccountProfiles
                        (Msg.PaymentCommandProcessing >> dispatch >> close)
                  | _ -> Html.progress []
               ]
               |> ScreenOverlay.Portal
            | Routes.PaymentUrl.ViewPayment payId ->
               classyNode Html.article [ "form-wrapper" ] [
                  Html.h6 "Payment"

                  CloseButton.render close

                  match state.Payments with
                  | Deferred.InProgress -> Html.progress []
                  | _ ->
                     match
                        accountProfilesOpt, selectedPayment state.Payments payId
                     with
                     | Some accountProfiles, Some payment ->
                        PaymentDetailComponent
                           session
                           payment
                           accountProfiles
                           (Msg.PaymentCommandProcessing >> dispatch >> close)
                     | _ -> Html.p $"No payment found for {payId}"
               ]
               |> ScreenOverlay.Portal
            | _ -> ()
         ]
      ]
   ]
