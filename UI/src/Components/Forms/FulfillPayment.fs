module Bank.Account.Forms.PaymentFulfillment

open Feliz
open Fable.Form.Simple
open System

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Transfer.Domain
open Bank.Employee.Domain
open FormContainer
open Lib.SharedTypes

type Values = { AccountIdSourceOfFunds: string }

let formFulfillPlatformPayment
   (payerAccounts: Map<AccountId, AccountProfile>)
   (payment: PlatformPayment)
   (initiatedBy: InitiatedById)
   : Form.Form<Values, Msg<Values>, IReactProperty>
   =
   let fundingSourceOptions =
      payerAccounts
      |> Map.toList
      |> List.map (fun (accountId, a) ->
         string accountId, $"{a.Name} ({Money.format a.Balance})")
      |> List.sortBy snd

   let fieldFundingSourceSelect =
      Form.selectField {
         Parser = Ok
         Value = _.AccountIdSourceOfFunds
         Update =
            fun newValue values -> {
               values with
                  AccountIdSourceOfFunds = newValue
            }
         Error = fun _ -> None
         Attributes = {
            Label = "Pay with account:"
            Placeholder = "No account selected"
            Options = fundingSourceOptions
         }
      }

   let onSubmit (selectedFundingSourceAccountId: string) =
      let selectedAccountId =
         selectedFundingSourceAccountId |> Guid.Parse |> AccountId

      let cmd =
         FulfillPlatformPaymentCommand.create initiatedBy {
            PaymentMethod = selectedAccountId
            RequestedPayment = {
               PlatformPaymentRequested.fromPayment payment with
                  BaseInfo.InitiatedById = initiatedBy
            }
         }
         |> AccountCommand.FulfillPlatformPayment

      Msg.GetAccountAndSubmit(selectedAccountId, cmd)

   Form.succeed onSubmit |> Form.append fieldFundingSourceSelect

[<ReactComponent>]
let PaymentFulfillmentFormComponent
   (session: UserSession)
   (payerAccounts: Map<AccountId, AccountProfile>)
   (payment: Payment)
   (onSubmit: ParentOnSubmitHandler)
   =
   let defaultSourceAccountId =
      payerAccounts
      |> Map.toSeq
      |> Seq.tryHead
      |> Option.map (snd >> _.AccountId >> string)
      |> Option.defaultValue ""

   let initValues = {
      AccountIdSourceOfFunds = defaultSourceAccountId
   }

   let initiatedBy = InitiatedById session.EmployeeId

   React.fragment [
      match payment with
      | Payment.ThirdParty _ -> Html.p "Not implemented."
      | Payment.Platform payment ->
         Html.select [
            attr.onChange (fun (_: string) -> ())
            attr.value "platform"

            attr.children [
               Html.option [
                  attr.value "platform"
                  attr.text "Pay with Platform"
                  attr.disabled true
               ]

               Html.option [
                  attr.value "ach"
                  attr.text "Pay with ACH"
                  attr.disabled true
               ]
            ]
         ]

         AccountFormContainer
            initValues
            (formFulfillPlatformPayment payerAccounts payment initiatedBy)
            onSubmit
   ]
