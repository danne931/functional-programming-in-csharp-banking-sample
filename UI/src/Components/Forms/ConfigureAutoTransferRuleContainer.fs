module Bank.Account.Forms.ConfigureAutomaticTransferFormContainer

open Feliz
open Feliz.Router
open Fable.Form.Simple
open Elmish
open Feliz.UseElmish

open Fable.Form.Simple.Pico
open Bank.Account.Domain
open Bank.Transfer.Domain
open Lib.SharedTypes
open UIDomain.Account

[<RequireQualifiedAccess>]
type State<'Values, 'FormResult> =
   | FillingForm of Form.View.Model<'Values>
   | DisplayingCalculation of Form.View.Model<'Values> * 'FormResult

type Msg<'Values, 'FormResult> =
   | FormChanged of Form.View.Model<'Values>
   | GoBackToFormFilling
   | DisplayCalculation of 'FormResult
   | Submit of
      Account *
      AccountCommand *
      AsyncOperationStatus<Result<AccountCommandReceipt, Err>>
   | ExternalError of string

let init (values: 'Values) () =
   values |> Form.View.idle |> State.FillingForm, Cmd.none

let update
   (onSubmit: AccountCommandReceipt -> unit)
   (msg: Msg<'Values, 'FormResult>)
   (state: State<'Values, 'FormResult>)
   =
   match msg with
   | FormChanged formModel ->
      match state with
      | State.FillingForm _ -> State.FillingForm formModel, Cmd.none
      | _ -> state, Cmd.none
   | DisplayCalculation formResult ->
      match state with
      | State.FillingForm model ->
         let model = {
            model with
               State = Form.View.State.Idle
         }

         State.DisplayingCalculation(model, formResult), Cmd.none
      | _ -> state, Cmd.none
   | GoBackToFormFilling ->
      match state with
      | State.DisplayingCalculation(model, _) ->
         State.FillingForm model, Cmd.none
      | _ -> state, Cmd.none
   | Submit(account, command, Started) ->
      let submit = async {
         let! res = AccountService.submitCommand account command
         return Submit(account, command, Finished res)
      }

      match state with
      | State.DisplayingCalculation(model, _) ->
         model |> Form.View.setLoading |> State.FillingForm,
         Cmd.fromAsync submit
      | _ -> state, Cmd.none
   | Submit(_, _, Finished(Ok receipt)) ->
      onSubmit receipt
      state, Cmd.none
   | Submit(_, command, Finished(Error err)) ->
      Log.error $"Error submitting command {command} {err}"

      match state with
      | State.DisplayingCalculation(model, formResult) ->
         let model = {
            model with
               State = Form.View.State.Error err.HumanFriendly
         }

         State.DisplayingCalculation(model, formResult), Alerts.toastCommand err
      | _ -> state, Cmd.none
   | ExternalError errMsg ->
      match state with
      | State.FillingForm(model) ->
         let model = {
            model with
               State = Form.View.State.Error errMsg
         }

         State.FillingForm model, Cmd.none
      | _ -> state, Cmd.none

let close _ =
   Router.navigate Routes.AccountUrl.AutoBalanceManagementPath

let renderError (msg: string) =
   Html.small [ attr.className "invalid"; attr.text msg ]

[<ReactComponent>]
let ConfigureAutoTransferRuleFormContainer
   (props:
      {|
         // Handle command submit success from the parent.
         OnSubmitSuccess: AccountCommandReceipt -> unit
         // Create a ConfigureAutoTransferRuleCommand from the 'FormResult.
         // The command will be sent to the AccountActor upon form submit.
         GenerateCommand:
            'FormResult -> Account * ConfigureAutoTransferRuleCommand
         // Indicate whether to display the calculation or notify user of
         // no changes to the original rule.
         RuleIsUnchanged: 'FormResult -> bool
         IntendToUpdateExistingRule: bool
         // Render the calculation based on the 'FormResult
         RenderCalculationDisplay: 'FormResult -> ReactElement
         // Initial values to pre-fill the form with in case of updating an
         // existing rule.
         Values: 'Values
         Form: Form.Form<'Values, Msg<'Values, 'FormResult>, IReactProperty>
         // Custom action to allow user to override typical
         // submit buttons & behavior.
         Action:
            (Form.View.Model<'Values>
               -> Form.View.Action<Msg<'Values, 'FormResult>>) option
         // Validate fields on blur or submit
         Validation: Form.View.Validation
      |})
   =
   let onSubmitSuccess = props.OnSubmitSuccess
   let generateCommand = props.GenerateCommand
   let ruleIsUnchanged = props.RuleIsUnchanged
   let intendToUpdateExistingRule = props.IntendToUpdateExistingRule
   let renderCalculationDisplay = props.RenderCalculationDisplay
   let initValues = props.Values

   let state, dispatch =
      React.useElmish (init initValues, update (onSubmitSuccess >> close), [||])

   match state with
   | State.FillingForm model ->
      Form.View.asHtml
         {
            Dispatch = dispatch
            OnChange = Msg.FormChanged
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
            Validation = props.Validation
         }
         props.Form
         model
   | State.DisplayingCalculation(model, formResult) ->
      let goBack () = dispatch Msg.GoBackToFormFilling

      React.fragment [
         if (intendToUpdateExistingRule && ruleIsUnchanged formResult) then
            Html.hr []
            Html.br []

            Html.p
               "No changes made to the rule so no updates will be submitted."

            Form.View.backButton goBack model.State
         else
            renderCalculationDisplay formResult

            classyNode Html.div [ "grid"; "form-submit-controls" ] [
               Form.View.group [
                  Form.View.backButton goBack model.State

                  Html.button [
                     attr.text (
                        if intendToUpdateExistingRule then
                           "Update Rule"
                        else
                           "Create Rule"
                     )

                     attr.onClick (fun e ->
                        e.preventDefault ()
                        let account, cmd = generateCommand formResult
                        let cmd = AccountCommand.ConfigureAutoTransferRule cmd
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
