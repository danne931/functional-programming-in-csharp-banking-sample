module Bank.Account.Forms.ConfigureAutomaticTransferZeroBalanceRule

open Feliz
open Fable.Form.Simple
open System

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Transfer.Domain
open Bank.Employee.Domain
open Lib.SharedTypes
open AutomaticTransfer
open UIDomain.Account
open Bank.Account.Forms.ConfigureAutomaticTransferFormContainer

type Values = {
   TargetAccountId: string
   DestinationAccountId: string
}

let form
   (accounts: Map<AccountId, Account>)
   (existingTargetAccountId: AccountId option)
   : Form.Form<Values, Result<FormResult, Err>, IReactProperty>
   =
   let fieldTargetAccountSelect (values: Values) =
      let optionIsDestination (a: Account) =
         string a.AccountId = values.DestinationAccountId

      let isSelectable (a: Account) =
         match existingTargetAccountId with
         | None -> a.AutoTransferRule.IsNone && not (optionIsDestination a)
         // Updating the target of a rule which has already been persisted
         // is not allowed.  The user needs to delete the existing rule if
         // they want to change the target account of a rule.
         | Some id -> id = a.AccountId

      Form.selectField {
         Parser =
            Form.Util.accountParser accounts "target account"
            >> validationErrorsHumanFriendly
         Value = fun values -> values.TargetAccountId
         Update =
            fun newValue values -> {
               values with
                  TargetAccountId = newValue
            }
         Error = fun _ -> None
         Attributes = {
            Label = "Maintain a zero balance for"
            Placeholder = "Select an account"
            Options = Form.Util.accountSelectOptions isSelectable accounts
         }
      }

   let fieldDestinationAccountSelect (values: Values) =
      Form.selectField {
         Parser =
            Form.Util.accountParser accounts "destination account"
            >> validationErrorsHumanFriendly
         Value = fun values -> values.DestinationAccountId
         Update =
            fun newValue values -> {
               values with
                  DestinationAccountId = newValue
            }
         Error = fun _ -> None
         Attributes = {
            Label = "Move money to"
            Placeholder = "Select an account"
            Options =
               Form.Util.accountSelectOptions
                  (fun a -> string a.AccountId <> values.TargetAccountId)
                  accounts
         }
      }

   let renderCalculation (target: Account, destination: Account) =
      let rule = {
         Sender = {
            Name = target.Name
            AccountId = target.AccountId
            OrgId = target.OrgId
         }
         Recipient = {
            Name = destination.Name
            AccountId = destination.AccountId
            OrgId = destination.OrgId
         }
      }

      Ok {
         Target = target
         Rule = AutomaticTransferRule.ZeroBalance rule
      }

   Form.succeed renderCalculation
   |> Form.append (
      Form.meta (fun values ->
         Form.succeed (fun target partner -> target, partner)
         |> Form.append (fieldTargetAccountSelect values)
         |> Form.append (fieldDestinationAccountSelect values))
      |> Form.group
   )

let renderCalculationDisplay (target: Account) (rule: ZeroBalanceRule) =
   let targetName = rule.Sender.Name
   let recipientName = rule.Recipient.Name

   React.fragment [
      Html.small Frequency.PerTransaction.Display

      Html.p $"Move all incoming money from {targetName} to {recipientName}."

      Html.small "Estimated 1st transfer"
      Html.hr []

      match PositiveAmount.create target.Balance with
      | None ->
         Html.p
            $"Balance of {targetName} is currently zero, 
              so no initial transfer is needed."
      | Some balance ->
         let amount =
            ZeroBalanceRule.computeTransfer rule balance
            |> _.Amount
            |> PositiveAmount.get
            |> Money.format

         classyNode Html.div [ "grid" ] [
            Html.p targetName
            Html.p [ attr.classes [ "debit" ]; attr.text $"-{amount}" ]
         ]

         classyNode Html.div [ "grid" ] [
            Html.p recipientName
            Html.p [ attr.classes [ "credit" ]; attr.text amount ]
         ]
   ]

[<ReactComponent>]
let ConfigureAutoTransferZeroBalanceRuleFormComponent
   (onSubmit: AccountCommandReceipt -> unit)
   (session: UserSession)
   (accounts: Map<AccountId, Account>)
   (ruleToEdit: (Guid * ZeroBalanceRule) option)
   =
   let existingRule = ruleToEdit |> Option.map snd
   let existingTargetAccountId = existingRule |> Option.map _.Sender.AccountId

   let initValues = {
      TargetAccountId =
         Form.Util.defaultTargetAccountId existingTargetAccountId accounts
      DestinationAccountId =
         existingRule
         |> Option.map (_.Recipient.AccountId >> string)
         |> Option.defaultValue ""
   }

   ConfigureAutoTransferRuleFormContainer {|
      Accounts = accounts
      Session = session
      ExistingRule =
         ruleToEdit
         |> Option.map (fun (ruleId, rule) ->
            ruleId, AutomaticTransferRule.ZeroBalance rule)
      OnSubmitSuccess = onSubmit
      RenderCalculationDisplay =
         fun formResult ->
            match formResult.Rule with
            | AutomaticTransferRule.ZeroBalance r ->
               renderCalculationDisplay formResult.Target r
            | _ -> Html.none
      Values = initValues
      Form = form accounts existingTargetAccountId
      Action = None
   |}
