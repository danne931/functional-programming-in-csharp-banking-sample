module Bank.Account.Forms.ConfigureAutomaticTransferFormContainer

open Feliz
open Feliz.Router
open Fable.Form.Simple
open Elmish
open Feliz.UseElmish
open System

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Transfer.Domain
open Bank.Employee.Domain
open Lib.SharedTypes
open UIDomain.Account
open AutomaticTransfer

type FormResult = {
   Target: Account
   Rule: AutomaticTransferRule
}

[<RequireQualifiedAccess>]
type State<'Values> =
   | FillingForm of Form.View.Model<'Values>
   | DisplayingCalculation of Form.View.Model<'Values> * FormResult

type S<'Values> = {
   DetectExternalErrorOnChange: bool
   State: State<'Values>
}

type Msg<'Values> =
   | FormChanged of Form.View.Model<'Values>
   | GoBackToFormFilling
   | DisplayCalculation of FormResult
   | Submit of
      Account *
      AccountCommand *
      AsyncOperationStatus<Result<AccountCommandReceipt, Err>>
   | ExternalError of string

let init (values: 'Values) () =
   {
      DetectExternalErrorOnChange = false
      State = values |> Form.View.idle |> State.FillingForm
   },
   Cmd.none

let update
   (onSubmit: AccountCommandReceipt -> unit)
   (msg: Msg<'Values>)
   (s: S<'Values>)
   =
   let update (state: State<'Values>) = { s with State = state }
   let state = s.State

   match msg with
   | FormChanged formModel ->
      match state with
      | State.FillingForm _ -> update (State.FillingForm formModel), Cmd.none
      | _ -> s, Cmd.none
   | DisplayCalculation formResult ->
      match state with
      | State.FillingForm model ->
         update (State.DisplayingCalculation(model, formResult)), Cmd.none
      | _ -> s, Cmd.none
   | GoBackToFormFilling ->
      match state with
      | State.DisplayingCalculation(model, _) ->
         update (State.FillingForm model), Cmd.none
      | _ -> s, Cmd.none
   | Submit(account, command, Started) ->
      let submit = async {
         let! res = AccountService.submitCommand account command
         return Submit(account, command, Finished res)
      }

      match state with
      | State.DisplayingCalculation(model, _) ->
         update (model |> Form.View.setLoading |> State.FillingForm),
         Cmd.fromAsync submit
      | _ -> s, Cmd.none
   | Submit(_, _, Finished(Ok receipt)) ->
      onSubmit receipt
      s, Cmd.none
   | Submit(_, command, Finished(Error err)) ->
      Log.error $"Error submitting command {command} {err}"

      match state with
      | State.DisplayingCalculation(model, formResult) ->
         let model = {
            model with
               State = Form.View.State.Error err.HumanFriendly
         }

         update (State.DisplayingCalculation(model, formResult)),
         Alerts.toastCommand err
      | _ -> s, Cmd.none
   | ExternalError errMsg ->
      match state with
      | State.FillingForm(model) ->
         let model = {
            model with
               State = Form.View.State.Error errMsg
         }

         {
            s with
               DetectExternalErrorOnChange = true
               State = State.FillingForm model
         },
         Cmd.none
      | _ -> s, Cmd.none

let close _ =
   Router.navigate Routes.AccountUrl.AutoBalanceManagementPath

let renderError (msg: string) =
   Html.small [ attr.className "invalid"; attr.text msg ]

let private cyclicTransferErrorMsg =
   "You may not add a rule which would create cyclic transfers."

let private hasCycle
   (accounts: Map<AccountId, Account>)
   (newRule: AutomaticTransferConfig)
   =
   accounts.Values
   |> Seq.toList
   |> List.choose _.AutoTransferRule
   |> CycleDetection.cycleDetected newRule

let private modelWithExternalErrorMaybe
   (accounts: Map<AccountId, Account>)
   (form: Form.Form<'Values, Result<FormResult, Err>, IReactProperty>)
   (existingRuleId: Guid option)
   (model: Form.View.Model<'Values>)
   =
   let filled = Form.fill form model.Values

   match filled.Result with
   | Ok res ->
      match res with
      | Ok parsedResult ->
         let ruleConfig = {
            Id = existingRuleId |> Option.defaultValue (Guid.NewGuid())
            Info = parsedResult.Rule
         }

         if hasCycle accounts ruleConfig then
            {
               model with
                  State = Form.View.State.Error cyclicTransferErrorMsg
            }
         else
            {
               model with
                  State = Form.View.State.Idle
            }
      | Error _ -> model
   | Error _ -> model

[<ReactComponent>]
let ConfigureAutoTransferRuleFormContainer
   (props:
      {|
         // Handle command submit success from the parent.
         OnSubmitSuccess: AccountCommandReceipt -> unit
         // Render the calculation based on the FormResult
         RenderCalculationDisplay: FormResult -> ReactElement
         // Initial values to pre-fill the form with in case of updating an
         // existing rule.
         Values: 'Values
         Form: Form.Form<'Values, Result<FormResult, Err>, IReactProperty>
         // Custom action to allow user to override typical
         // submit buttons & behavior.
         Action:
            (Form.View.Model<'Values> -> Form.View.Action<Msg<'Values>>) option
         Session: UserSession
         Accounts: Map<AccountId, Account>
         ExistingRule: (Guid * AutomaticTransferRule) option
      |})
   =
   let onSubmitSuccess = props.OnSubmitSuccess
   let renderCalculationDisplay = props.RenderCalculationDisplay
   let initValues = props.Values
   let accounts = props.Accounts
   let existingRuleId = props.ExistingRule |> Option.map fst
   let existingRule = props.ExistingRule |> Option.map snd

   let modelWithExternalErrorMaybe =
      modelWithExternalErrorMaybe accounts props.Form existingRuleId

   let state, dispatch =
      React.useElmish (init initValues, update (onSubmitSuccess >> close), [||])

   let form =
      Form.succeed (fun (res: Result<FormResult, Err>) ->
         match res with
         | Ok formResult ->
            let ruleConfig = {
               Id = existingRuleId |> Option.defaultValue (Guid.NewGuid())
               Info = formResult.Rule
            }

            if hasCycle accounts ruleConfig then
               Msg.ExternalError cyclicTransferErrorMsg
            else
               Msg.DisplayCalculation formResult
         | Error e -> Msg.ExternalError(string e))
      |> Form.append props.Form

   match state.State with
   | State.FillingForm model ->
      Form.View.asHtml
         {
            Dispatch = dispatch
            OnChange =
               fun model ->
                  // Display potential cyclic transfer error on change after the
                  // submit button pressed for first time, keeping in line with
                  // Form.View.Validation.ValidateOnSubmit functionality.
                  let model =
                     if state.DetectExternalErrorOnChange then
                        modelWithExternalErrorMaybe model
                     else
                        model

                  Msg.FormChanged model
            Action =
               props.Action
               |> Option.map (fun act -> act model)
               |> Option.defaultValue (
                  Form.View.Action.Custom(fun state _ ->
                     React.fragment [
                        // Render other external error if present
                        match state with
                        | Form.View.State.Error errMsg ->
                           classyNode Html.div [
                              "form-external-error-container"
                           ] [ renderError errMsg ]
                        | _ -> ()

                        Form.View.submitAndCancelButton
                           "Calculate 1st transfer"
                           close
                           state
                     ])
               )
            Validation = Form.View.Validation.ValidateOnSubmit
         }
         form
         model
   | State.DisplayingCalculation(model, formResult) ->
      let goBack () = dispatch Msg.GoBackToFormFilling

      React.fragment [
         match existingRule with
         | Some existing when existing = formResult.Rule ->
            Html.hr []
            Html.br []

            Html.p
               "No changes made to the rule so no updates will be submitted."

            Form.View.backButton goBack model.State
         | _ ->
            renderCalculationDisplay formResult

            classyNode Html.div [ "grid"; "form-submit-controls" ] [
               Form.View.group [
                  Form.View.backButton goBack model.State

                  Html.button [
                     attr.text (
                        if existingRule.IsSome then
                           "Update Rule"
                        else
                           "Create Rule"
                     )

                     attr.onClick (fun e ->
                        e.preventDefault ()
                        let account = formResult.Target

                        let cmd =
                           ConfigureAutoTransferRuleCommand.create
                              account.CompositeId
                              (InitiatedById props.Session.EmployeeId)
                              {
                                 RuleIdToUpdate = existingRuleId
                                 Rule = formResult.Rule
                              }
                           |> AccountCommand.ConfigureAutoTransferRule

                        dispatch <| Msg.Submit(account, cmd, Started))

                     match model.State with
                     | Form.View.State.Loading ->
                        attr.text "Processing..."
                        attr.ariaBusy true
                     | _ -> ()
                  ]
               ]
            ]
      ]
