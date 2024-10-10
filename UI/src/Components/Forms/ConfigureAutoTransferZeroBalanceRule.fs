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

type FormResult = {
   SelectedTarget: Account
   Rule: ZeroBalanceRule
}

type Values = {
   TargetAccountId: string
   DestinationAccountId: string
}

let form
   (accounts: Map<AccountId, Account>)
   (existingTargetAccountId: AccountId option)
   : Form.Form<Values, Msg<Values, FormResult>, IReactProperty>
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

      let hasCycle =
         accounts.Values
         |> Seq.toList
         |> List.choose (_.AutoTransferRule >> Option.map _.Info)
         |> CycleDetection.cycleDetected (
            AutomaticTransferRule.ZeroBalance rule
         )

      if hasCycle then
         Msg.ExternalError
            "You may not add a rule which would create cyclic transfers."
      else
         Msg.DisplayCalculation { SelectedTarget = target; Rule = rule }

   Form.succeed renderCalculation
   |> Form.append (
      Form.meta (fun values ->
         Form.succeed (fun target partner -> target, partner)
         |> Form.append (fieldTargetAccountSelect values)
         |> Form.append (fieldDestinationAccountSelect values))
      |> Form.group
   )

let renderCalculationDisplay (formResult: FormResult) =
   let rule = formResult.Rule
   let targetName = rule.Sender.Name
   let recipientName = rule.Recipient.Name

   React.fragment [
      Html.small Frequency.PerTransaction.Display

      Html.p $"Move all incoming money from {targetName} to {recipientName}."

      Html.small "Estimated 1st transfer"
      Html.hr []

      match PositiveAmount.create formResult.SelectedTarget.Balance with
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
   let existingRuleId = ruleToEdit |> Option.map fst
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
      OnSubmitSuccess = onSubmit
      GenerateCommand =
         fun result ->
            result.SelectedTarget,
            ConfigureAutoTransferRuleCommand.create
               result.SelectedTarget.CompositeId
               (InitiatedById session.EmployeeId)
               {
                  RuleIdToUpdate = existingRuleId
                  Rule = AutomaticTransferRule.ZeroBalance result.Rule
               }
      IntendToUpdateExistingRule = existingRule.IsSome
      RuleIsUnchanged = fun result -> existingRule = Some result.Rule
      RenderCalculationDisplay = renderCalculationDisplay
      Values = initValues
      Form = form accounts existingTargetAccountId
      Validation = Form.View.Validation.ValidateOnSubmit
      Action = None
   |}
