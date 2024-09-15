module Bank.Account.Forms.PaymentRequest

open Feliz
open Fable.Form.Simple
open System

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Transfer.Domain
open Bank.Employee.Domain
open Lib.Validators
open FormContainer
open Lib.SharedTypes

type Values = {
   Amount: string
   PayeeAccountId: string
   PayerOrgId: string
   Memo: string
   Expiration: string
}

let amountField =
   Form.textField {
      Parser =
         amountValidatorFromString "Payment amount"
         >> validationErrorsHumanFriendly
      Value = _.Amount
      Update = fun newValue values -> { values with Amount = newValue }
      Error = fun _ -> None
      Attributes = {
         Label = "Amount:"
         Placeholder = "1300"
         HtmlAttributes = []
      }
   }

let memoField =
   Form.textField {
      Parser = Ok
      Value = _.Memo
      Update = fun newValue values -> { values with Memo = newValue }
      Error = fun _ -> None
      Attributes = {
         Label = "Memo:"
         Placeholder = "Reason for Payment Request"
         HtmlAttributes = []
      }
   }

let expirationField =
   Form.dateField {
      Parser =
         CustomDateInterpreter.validate
            CustomDateInterpreter.DateSignifier.Start
         >> Result.bind (
            snd
            >> dateInFutureValidator "Expiration"
            >> validationErrorsHumanFriendly
         )
      Value = _.Expiration
      Update = fun newValue values -> { values with Expiration = newValue }
      Error = fun _ -> None
      Attributes = {
         Label = "Expiration:"
         Placeholder = "Payment request expires on"
         HtmlAttributes = []
      }
   }

let expirationForm =
   Form.succeed id |> Form.append expirationField |> Form.optional

let formPlatformPayment
   (payeeOrg: Org)
   (payeeDestinationAccounts: Map<AccountId, AccountProfile>)
   (orgs: Org list)
   (initiatedBy: InitiatedById)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let orgPayerOptions =
      orgs
      |> List.choose (fun org ->
         org.Permissions.SocialTransferDiscoveryPrimaryAccountId
         |> Option.map (fun _ -> string org.OrgId, org.Name))
      |> List.sortBy snd

   let fieldOrgPayerSelect =
      Form.selectField {
         Parser = Ok
         Value = _.PayerOrgId
         Update = fun newValue values -> { values with PayerOrgId = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Payer Organization:"
            Placeholder = "No organization selected"
            Options = orgPayerOptions
         }
      }

   let payeeAccountOptions =
      payeeDestinationAccounts
      |> Map.toList
      |> List.map (fun (accountId, a) ->
         string accountId, $"{a.Name} ({Money.format a.Balance})")
      |> List.sortBy snd

   let fieldPayeeAccountSelect =
      Form.selectField {
         Parser = Ok
         Value = _.PayeeAccountId
         Update =
            fun newValue values -> {
               values with
                  PayeeAccountId = newValue
            }
         Error = fun _ -> None
         Attributes = {
            Label = "Payee Destination Account:"
            Placeholder = "No account selected"
            Options = payeeAccountOptions
         }
      }

   let onSubmit
      (selectedPayerOrgId: string)
      (amount: decimal)
      (memo: string)
      (expirationOpt: DateTime option)
      (selectedDestinationAccountId: string)
      =
      let payerOrg =
         orgs |> List.find (fun o -> string o.OrgId = selectedPayerOrgId)

      let payeeAccountId =
         selectedDestinationAccountId |> Guid.Parse |> AccountId

      let payeeAccount = payeeDestinationAccounts[payeeAccountId]

      let cmd =
         RequestPlatformPaymentCommand.create
            payeeAccount.CompositeId
            initiatedBy
            {
               Expiration = expirationOpt |> Option.map _.ToUniversalTime()
               Memo = memo
               BaseInfo = {
                  Id = Guid.NewGuid() |> PaymentId
                  InitiatedById = initiatedBy
                  Amount = amount
                  Payer = {
                     OrgId = payerOrg.OrgId
                     OrgName = payerOrg.Name
                  }
                  Payee = {
                     OrgId = payeeOrg.OrgId
                     OrgName = payeeOrg.Name
                     AccountId = payeeAccountId
                  }
               }
            }
         |> AccountCommand.RequestPlatformPayment

      Msg.GetAccountAndSubmit(payeeAccountId, cmd)

   Form.succeed onSubmit
   |> Form.append fieldOrgPayerSelect
   |> Form.append amountField
   |> Form.append memoField
   |> Form.append expirationForm
   |> Form.append fieldPayeeAccountSelect

[<ReactComponent>]
let PaymentRequestFormComponent
   (session: UserSession)
   (payeeOrg: Org)
   (payeeDestinationAccounts: Map<AccountId, AccountProfile>)
   (onSubmit: ParentOnSubmitHandler)
   =
   let defaultDestinationAccount =
      payeeDestinationAccounts
      |> Map.toSeq
      |> Seq.tryHead
      |> Option.map (snd >> _.AccountId >> string)
      |> Option.defaultValue ""

   let initValues = {
      Amount = ""
      PayeeAccountId = defaultDestinationAccount
      PayerOrgId = ""
      Memo = ""
      Expiration = ""
   }

   let initiatedBy = InitiatedById session.EmployeeId

   let selectedPaymentType, setSelectedPaymentType =
      React.useState PaymentType.Platform

   React.fragment [
      Html.select [
         attr.onChange (PaymentType.fromStringUnsafe >> setSelectedPaymentType)
         attr.value (string selectedPaymentType)

         attr.children [
            Html.option [
               attr.value (string PaymentType.Platform)
               attr.text "To org on the platform"
            ]

            Html.option [
               attr.value (string PaymentType.ThirdParty)
               attr.text "To org/person outside the platform"
               attr.disabled true
            ]
         ]
      ]

      match selectedPaymentType with
      | PaymentType.Platform ->
         OrgSocialTransferDiscovery.OrgSearchComponent
            session.OrgId
            (fun searchInput orgs ->
               match orgs with
               | Deferred.InProgress -> Html.progress []
               | Deferred.Resolved(Ok(Some orgs)) ->
                  let initValues =
                     orgs
                     |> List.tryHead
                     |> Option.map (fun o -> {
                        initValues with
                           PayerOrgId = string o.OrgId
                     })
                     |> Option.defaultValue initValues

                  AccountFormContainer
                     initValues
                     (formPlatformPayment
                        payeeOrg
                        payeeDestinationAccounts
                        orgs
                        initiatedBy)
                     onSubmit
               | Deferred.Resolved(Ok None) ->
                  Html.p $"No orgs found by search query {searchInput}."
               | _ -> Html.none)
      | PaymentType.ThirdParty -> Html.p "Not implemented."
   ]
