module AutomaticTransferRule

open Feliz
open Elmish
open Feliz.UseElmish
open Feliz.Router
open Elmish.SweetAlert

open Bank.Account.Domain
open Bank.Employee.Domain
open Bank.Transfer.Domain
open Lib.SharedTypes
open AutomaticTransfer
open UIDomain.Account
open Dropdown

type State = {
   DeleteProgress: Deferred<Result<AccountCommandReceipt, Err>>
}

type Msg =
   | DismissConfirmation
   | ShowDeleteRuleConfirmation of Account * AutomaticTransferConfig
   | ConfirmDeleteRule of
      Account *
      AutomaticTransferConfig *
      AsyncOperationStatus<Result<AccountCommandReceipt, Err>>

let init () =
   { DeleteProgress = Deferred.Idle }, Cmd.none

let update
   (onDeleted: AccountCommandReceipt -> unit)
   (session: UserSession)
   msg
   (state: State)
   =
   match msg with
   | DismissConfirmation -> state, Cmd.none
   | ShowDeleteRuleConfirmation(account, ruleConfig) ->
      let confirm =
         ConfirmAlert(
            "Are you sure you want to delete this rule?",
            function
            | ConfirmAlertResult.Confirmed ->
               Msg.ConfirmDeleteRule(account, ruleConfig, Started)
            | ConfirmAlertResult.Dismissed _ -> Msg.DismissConfirmation
         )
            .ConfirmButtonText("Yes")
            .Type(AlertType.Question)
            .ShowCloseButton(true)

      state, SweetAlert.Run confirm
   | ConfirmDeleteRule(account, ruleConfig, Started) ->
      let cmd =
         DeleteAutoTransferRuleCommand.create
            (account.AccountId, account.OrgId)
            (InitiatedById session.EmployeeId)
            { RuleId = ruleConfig.Id }
         |> AccountCommand.DeleteAutoTransferRule

      let delete = async {
         let! res = AccountService.submitCommand account cmd
         return Msg.ConfirmDeleteRule(account, ruleConfig, Finished res)
      }

      {
         state with
            DeleteProgress = Deferred.InProgress
      },
      Cmd.fromAsync delete
   | ConfirmDeleteRule(_, _, Finished(Ok receipt)) ->
      onDeleted receipt

      {
         state with
            DeleteProgress = Deferred.Idle
      },
      Alerts.toastSuccessCommand $"Deleted rule"
   | ConfirmDeleteRule(_, _, Finished(Error err)) ->
      Log.error (string err)

      {
         state with
            DeleteProgress = Deferred.Resolved(Error err)
      },
      Alerts.toastCommand err

let ruleDescription =
   function
   | AutomaticTransferRule.ZeroBalance r ->
      $"Move all from {r.Sender.Name} to {r.Recipient.Name}"
   | AutomaticTransferRule.TargetBalance r ->
      let targetBalance =
         r.TargetAccountBalance |> PositiveAmount.get |> Money.format

      $"Restore {r.TargetAccount.Name} to {targetBalance}"
   | AutomaticTransferRule.PercentDistribution r ->
      let r = PercentDistributionRule.get r
      let destinations = r.DestinationAccounts

      $"Distribute the balance of {r.Sender.Name}
        to {destinations.Length} accounts"

let frequencyDescription =
   function
   | AutomaticTransferRule.ZeroBalance _ -> Frequency.PerTransaction.Display
   | AutomaticTransferRule.TargetBalance _ ->
      (Frequency.Schedule CronSchedule.Daily).Display
   | AutomaticTransferRule.PercentDistribution r ->
      let r = PercentDistributionRule.get r
      r.Frequency.Display

[<ReactComponent>]
let AutoTransferRuleComponent
   (props:
      {|
         TargetAccount: Account
         AutoTransferConfig: AutomaticTransferConfig
         Session: UserSession
         onRuleDeleted: AccountCommandReceipt -> unit
         OnMouseEnter: unit -> unit
         OnMouseLeave: unit -> unit
      |})
   =
   let _, dispatch =
      React.useElmish (init, update props.onRuleDeleted props.Session, [||])

   let rule = props.AutoTransferConfig.Info

   Html.div [
      attr.onMouseEnter (fun _ -> props.OnMouseEnter())

      attr.onMouseLeave (fun _ -> props.OnMouseLeave())

      attr.children [
         Html.p (ruleDescription rule)
         Html.small (frequencyDescription rule)

         DropdownComponent {|
            Direction = DropdownDirection.RTL
            ShowCaret = false
            Button = None
            Items = [
               {
                  Text = "Edit Rule"
                  OnClick =
                     fun _ ->
                        Routes.AccountUrl.editRulePath
                           props.AutoTransferConfig.Id
                        |> Router.navigate
                  IsSelected = false
               }
               {
                  Text = "Remove Rule"
                  OnClick =
                     fun _ ->
                        dispatch
                        <| Msg.ShowDeleteRuleConfirmation(
                           props.TargetAccount,
                           props.AutoTransferConfig
                        )
                  IsSelected = false
               }
            ]
         |}
      ]
   ]
