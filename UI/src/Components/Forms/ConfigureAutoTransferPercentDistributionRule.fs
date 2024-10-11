module Bank.Account.Forms.ConfigureAutomaticTransferPercentDistributionRule

open Feliz
open Fable.Form.Simple
open System

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Transfer.Domain
open Bank.Employee.Domain
open Lib.SharedTypes
open Lib.Validators
open AutomaticTransfer
open UIDomain.Account
open Bank.Account.Forms.ConfigureAutomaticTransferFormContainer

type FormResult = {
   Target: Account
   Rule: PercentDistributionRule.T
}

type DestinationAccountValues = {
   AccountId: string
   PercentToAllocate: string
}

type Values = {
   TargetAccountId: string
   Destinations: DestinationAccountValues list
   Frequency: string
}

let percentAllocatedParser =
   parseDecimal "Percent allocated" >> validationErrorsHumanFriendly

let fieldPercentAllocated =
   Form.textField {
      Parser = percentAllocatedParser
      Value = _.PercentToAllocate
      Update =
         fun newValue values -> {
            values with
               PercentToAllocate =
                  Form.Util.formattedPositiveDecimal newValue
                  |> Option.defaultValue values.PercentToAllocate
         }
      Error = fun _ -> None
      Attributes = {
         Label = "Percent:"
         Placeholder = "Percent allocated"
         HtmlAttributes = []
      }
   }

let fieldFrequency =
   Form.radioField {
      Parser =
         fun input ->
            match Frequency.fromString input with
            | Some f -> Ok f
            | None -> Error $"Invalid frequency {input}"
      Value = fun values -> values.Frequency
      Update = fun newValue values -> { values with Frequency = newValue }
      Error = fun _ -> None
      Attributes = {
         Label = "Auto transfer frequency:"
         Options =
            let perT = Frequency.PerTransaction
            let daily = Frequency.Schedule CronSchedule.Daily
            let twiceMonthly = Frequency.Schedule CronSchedule.TwiceMonthly

            [
               string perT, perT.Display
               string daily, daily.Display
               string twiceMonthly, twiceMonthly.Display
            ]
      }
   }

let destinationAccountForm
   (accounts: Map<AccountId, Account>)
   (values: Values)
   (index: int)
   : Form.Form<DestinationAccountValues, _, _>
   =
   let optionIsTarget (account: Account) =
      string account.AccountId = values.TargetAccountId

   let optionIsDestination (a: Account) =
      values.Destinations
      |> List.tryFindIndex (fun d -> d.AccountId = string a.AccountId)
      |> Option.map (fun foundInd -> foundInd <> index)
      |> Option.defaultValue false

   let isSelectable (a: Account) =
      not (optionIsTarget a || optionIsDestination a)

   let fieldDestinationAccountSelect =
      Form.selectField {
         Parser =
            Form.Util.accountParser accounts "Destination account"
            >> validationErrorsHumanFriendly
         Value = fun values -> values.AccountId
         Update = fun newValue values -> { values with AccountId = newValue }
         Error = fun _ -> None
         Attributes = {
            Label = "Move money to:"
            Placeholder = "Select an account"
            Options = Form.Util.accountSelectOptions isSelectable accounts
         }
      }

   Form.succeed (fun proposedAllocation (destination: Account) -> {
      ProposedPercentAllocated = proposedAllocation
      Recipient = {
         OrgId = destination.OrgId
         AccountId = destination.AccountId
         Name = destination.Name
      }
   })
   |> Form.append fieldPercentAllocated
   |> Form.append fieldDestinationAccountSelect

let form
   (accounts: Map<AccountId, Account>)
   (existingSenderAccountId: AccountId option)
   : Form.Form<Values, Msg<Values, FormResult>, IReactProperty>
   =
   let fieldTargetAccountSelect (values: Values) =
      let optionIsDestination (a: Account) =
         values.Destinations
         |> List.exists (fun dest -> dest.AccountId = string a.AccountId)

      let optionHasRuleAlready = _.AutoTransferRule.IsSome

      let isSelectable (a: Account) =
         match existingSenderAccountId with
         | None -> not (optionHasRuleAlready a || optionIsDestination a)
         // Updating the sender of a rule which has already been persisted
         // is not allowed.  The user needs to delete the existing rule if
         // they want to change the sender account of a rule.
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
            Label = "Manage the balance of:"
            Placeholder = ""
            Options = Form.Util.accountSelectOptions isSelectable accounts
         }
      }

   let renderCalculation
      (targetAccount: Account)
      (destinations: UnvalidatedDistributionDestinationAccount list)
      (frequency: Frequency)
      =
      let ruleRes =
         PercentDistributionRule.create
            frequency
            {
               Name = targetAccount.Name
               AccountId = targetAccount.AccountId
               OrgId = targetAccount.OrgId
            }
            destinations

      match ruleRes with
      | Ok rule ->
         let hasCycle =
            accounts.Values
            |> Seq.toList
            |> List.choose (_.AutoTransferRule >> Option.map _.Info)
            |> CycleDetection.cycleDetected (
               AutomaticTransferRule.PercentDistribution rule
            )

         if hasCycle then
            Msg.ExternalError
               "You may not add a rule which would create cyclic transfers."
         else
            Msg.DisplayCalculation { Rule = rule; Target = targetAccount }
      | Error e -> Msg.ExternalError(string e)

   Form.succeed renderCalculation
   |> Form.append (Form.meta fieldTargetAccountSelect)
   |> Form.append (
      Form.meta (fun values ->
         let form = destinationAccountForm accounts values

         Form.list
            {
               Default = {
                  AccountId = ""
                  PercentToAllocate = "1"
               }
               Value = fun values -> values.Destinations
               Update =
                  fun newValue values -> {
                     values with
                        Destinations = newValue
                  }
               Attributes = {
                  Label = ""
                  Add =
                     // Disallow creating another list item if no more
                     // potential destination accounts available
                     if values.Destinations.Length < accounts.Count - 1 then
                        Some "Add another account"
                     else
                        None
                  Delete =
                     // Ensure 1 destination input always exists
                     if values.Destinations.Length <= 1 then None else Some ""
               }
            }
            form)
   )
   |> Form.append fieldFrequency

let renderCalculationDisplay (formResult: FormResult) =
   let rule = PercentDistributionRule.get formResult.Rule
   let targetName = rule.Sender.Name

   classyNode Html.div [ "auto-transfer-calculation-display" ] [
      Html.small rule.Frequency.Display

      Html.p (
         List.fold
            (fun acc dest ->
               acc
               + $$"""{{PositiveAmount.get dest.PercentAllocated}}% to {{dest.Recipient.Name}}, """)
            $"Distribute money from {targetName} to {rule.DestinationAccounts.Length} accounts: "
            rule.DestinationAccounts
         |> fun str -> str.Remove(str.Length - 2) + "."
      )

      Html.small "Estimated 1st transfer"
      Html.hr []

      match PositiveAmount.create formResult.Target.Balance with
      | None ->
         Html.p
            $"Balance of {targetName} is too low for distribution,
              so no transfer needed."
      | Some balance ->
         let computed =
            PercentDistributionRule.computeTransfer formResult.Rule balance

         let totalAmount =
            computed |> List.sumBy (_.Amount >> PositiveAmount.get)

         for t in computed do
            let amount = PositiveAmount.get t.Amount
            let percentToAllocate = (amount / totalAmount) * 100m

            classyNode Html.div [ "grid" ] [
               Html.p $$"""{{percentToAllocate}}% to {{t.Recipient.Name}}"""
               Html.p [
                  attr.classes [ "credit" ]
                  attr.text (Money.format amount)
               ]
            ]

         classyNode Html.div [ "grid" ] [
            Html.p targetName
            Html.p [
               attr.classes [ "debit" ]
               attr.text $"-{Money.format totalAmount}"
            ]
         ]
   ]

[<ReactComponent>]
let ConfigureAutoTransferPercentDistributionRuleFormComponent
   (onSubmit: AccountCommandReceipt -> unit)
   (session: UserSession)
   (accounts: Map<AccountId, Account>)
   (ruleToEdit: (Guid * PercentDistributionRule.T) option)
   =
   let existingRuleId = ruleToEdit |> Option.map fst

   let existingRule =
      ruleToEdit |> Option.map (snd >> PercentDistributionRule.get)

   let existingTargetAccountId = existingRule |> Option.map _.Sender.AccountId

   let initValues = {
      Frequency =
         existingRule
         |> Option.map (_.Frequency >> string)
         |> Option.defaultValue ""
      TargetAccountId =
         Form.Util.defaultTargetAccountId existingTargetAccountId accounts
      Destinations =
         existingRule
         |> Option.map (
            _.DestinationAccounts
            >> List.map (fun d -> {
               AccountId = string d.Recipient.AccountId
               PercentToAllocate =
                  d.PercentAllocated |> PositiveAmount.get |> string
            })
         )
         |> Option.defaultValue [
            {
               AccountId = ""
               PercentToAllocate = "100"
            }
         ]
   }

   ConfigureAutoTransferRuleFormContainer {|
      OnSubmitSuccess = onSubmit
      GenerateCommand =
         fun result ->
            result.Target,
            ConfigureAutoTransferRuleCommand.create
               result.Target.CompositeId
               (InitiatedById session.EmployeeId)
               {
                  RuleIdToUpdate = existingRuleId
                  Rule = AutomaticTransferRule.PercentDistribution result.Rule
               }
      IntendToUpdateExistingRule = existingRule.IsSome
      RuleIsUnchanged =
         fun result -> (ruleToEdit |> Option.map snd) = Some result.Rule
      RenderCalculationDisplay = renderCalculationDisplay
      Values = initValues
      Form = form accounts existingTargetAccountId
      Validation = Form.View.Validation.ValidateOnSubmit
      Action =
         Some(fun model ->
            Form.View.Action.Custom(fun state _ ->
               let totalPercentToAllocate =
                  model.Values.Destinations
                  |> List.sumBy (fun d ->
                     try
                        decimal d.PercentToAllocate
                     with _ ->
                        0m)

               React.fragment [
                  classyNode Html.div [ "percent-distribution-progress" ] [
                     Html.progress [
                        attr.value (int totalPercentToAllocate)
                        attr.max 100
                        if totalPercentToAllocate > 100m then
                           attr.className "invalid"
                     ]

                     if totalPercentToAllocate = 100m then
                        Html.small "100% allocated."

                        // Render other external error if present
                        match state with
                        | Form.View.State.Error errMsg ->
                           classyNode Html.div [
                              "form-external-error-container"
                           ] [ renderError errMsg ]
                        | _ -> ()
                     else
                        totalPercentToAllocate
                        |> PercentDistributionRule.ValidationError.TotalPercentAllocatedNot100
                        |> string
                        |> renderError
                  ]

                  Form.View.submitAndCancelButton
                     "Calculate 1st transfer"
                     close
                     state
               ]))
   |}
