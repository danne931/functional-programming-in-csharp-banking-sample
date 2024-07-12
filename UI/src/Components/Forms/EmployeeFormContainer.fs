module Bank.Employee.Forms.FormContainer

open Feliz
open Feliz.UseElmish
open Elmish
open Fable.Form.Simple

open Fable.Form.Simple.Pico
open Lib.SharedTypes
open Bank.Employee.Domain
open UIDomain.Employee

type ParentOnSubmitHandler = EmployeeCommandReceipt -> unit

type State<'Values> = { FormModel: Form.View.Model<'Values> }

type Msg<'Values> =
   | FormChanged of Form.View.Model<'Values>
   | Submit of
      Employee *
      EmployeeCommand *
      AsyncOperationStatus<Result<EmployeeCommandReceipt, Err>>
   | ErrorReceivedViaSignalR of Err

let init (values: 'Values) () =
   { FormModel = Form.View.idle values }, Cmd.none

let update
   (onSubmit: ParentOnSubmitHandler)
   (msg: Msg<'Values>)
   (state: State<'Values>)
   =
   match msg with
   | FormChanged formModel -> { state with FormModel = formModel }, Cmd.none
   | Submit(employee, command, Started) ->
      let submit = async {
         let! res = EmployeeService.submitCommand employee command
         return Submit(employee, command, Finished res)
      }

      {
         state with
            FormModel = state.FormModel |> Form.View.setLoading
      },
      Cmd.fromAsync submit
   | Submit(_, _, Finished(Ok receipt)) ->
      onSubmit receipt
      state, Cmd.none
   | Submit(_, command, Finished(Error err)) ->
      Log.error $"Error submitting command {command} {err}"

      {
         state with
            FormModel.State = Form.View.State.Error(err.HumanFriendly)
      },
      Alerts.toastCommand err
   | ErrorReceivedViaSignalR err ->
      {
         state with
            FormModel.State = Form.View.State.Error(err.HumanFriendly)
      },
      Alerts.toastCommand err

[<ReactComponent>]
let EmployeeFormContainer
   (values: 'Values)
   (form: Form.Form<'Values, Msg<'Values>, IReactProperty>)
   (onSubmit: ParentOnSubmitHandler)
   (action: Form.View.Action<Msg<'Values>> option)
   =
   let state, dispatch = React.useElmish (init values, update onSubmit, [||])

   Form.View.asHtml
      {
         Dispatch = dispatch
         OnChange = FormChanged
         Action =
            action |> Option.defaultValue (Form.View.Action.SubmitOnly "Submit")
         Validation = Form.View.ValidateOnSubmit
      }
      form
      state.FormModel
