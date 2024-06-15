module Bank.Account.Forms.FormContainer

open Feliz
open Feliz.UseElmish
open Elmish
open Fable.Form.Simple

open Fable.Form.Simple.Pico
open Lib.SharedTypes
open Bank.Account.Domain
open Bank.Employee.Domain
open AsyncUtil

[<RequireQualifiedAccess>]
type FormDomain =
   | Account of Account
   | Employee of Employee

[<RequireQualifiedAccess>]
type FormCommand =
   | Account of AccountCommand
   | Employee of EmployeeCommand

type ParentOnSubmitHandler = FormCommand * CommandProcessingResponse -> unit

type State<'Values> = { FormModel: Form.View.Model<'Values> }

type Msg<'Values> =
   | FormChanged of Form.View.Model<'Values>
   | Submit of
      FormCommand *
      AsyncOperationStatus<Result<CommandProcessingResponse, Err>>
   | ErrorReceivedViaSignalR of Err

let init (values: 'Values) () =
   { FormModel = Form.View.idle values }, Cmd.none

let update
   (persist: FormCommand -> Async<Result<CommandProcessingResponse, Err>>)
   (onSubmit: ParentOnSubmitHandler)
   (msg: Msg<'Values>)
   (state: State<'Values>)
   =
   match msg with
   | FormChanged formModel -> { state with FormModel = formModel }, Cmd.none
   | Submit(command, Started) ->
      let submit = async {
         let! res = persist command
         return Submit(command, Finished res)
      }

      {
         state with
            FormModel = state.FormModel |> Form.View.setLoading
      },
      Cmd.fromAsync submit
   | Submit(command, Finished(Ok eventId)) ->
      onSubmit (command, eventId)

      state, Cmd.none
   | Submit(command, Finished(Error err)) ->
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
let FormContainer
   (domain: FormDomain)
   (values: 'Values)
   (form: Form.Form<'Values, Msg<'Values>, IReactProperty>)
   (onSubmit: ParentOnSubmitHandler)
   =
   let persist (command: FormCommand) =
      match domain, command with
      | FormDomain.Account account, FormCommand.Account command ->
         AccountService.submitCommand account command
      | FormDomain.Employee employee, FormCommand.Employee command ->
         EmployeeService.submitCommand employee command
      | _ -> failwith $"FormContainer does not support mixed domains."

   let state, dispatch =
      React.useElmish (init values, update persist onSubmit, [||])

   let signalRContext = React.useContext SignalRConnectionProvider.context
   let errors = signalRContext.Errors

   React.useEffect (
      (fun () ->
         if
            state.FormModel.State = Form.View.State.Loading
            && errors.Length > 0
         then
            dispatch <| Msg.ErrorReceivedViaSignalR errors.Head.Error),
      [| box errors.Length |]
   )

   Form.View.asHtml
      {
         Dispatch = dispatch
         OnChange = FormChanged
         Action = Form.View.Action.SubmitOnly "Submit"
         Validation = Form.View.ValidateOnSubmit
      }
      form
      state.FormModel
