module Bank.Account.Forms.FormContainer

open Feliz
open Feliz.UseElmish
open Elmish
open Fable.Form.Simple

open Fable.Form.Simple.Pico
open Lib.SharedTypes
open Bank.Account.Domain
open AsyncUtil

type ParentOnSubmitHandler = AccountCommand * EventId -> unit

type State<'Values> = { FormModel: Form.View.Model<'Values> }

type Msg<'Values> =
   | FormChanged of Form.View.Model<'Values>
   | Submit of AccountCommand * AsyncOperationStatus<Result<EventId, Err>>
   | ErrorReceivedViaSignalR of Err

let init (values: 'Values) () =
   { FormModel = Form.View.idle values }, Cmd.none

let update
   (account: Account)
   (onSubmit: ParentOnSubmitHandler)
   (msg: Msg<'Values>)
   (state: State<'Values>)
   =
   match msg with
   | FormChanged formModel -> { state with FormModel = formModel }, Cmd.none
   | Submit(command, Started) ->
      let submit = async {
         let! res = AccountService.submitCommand account command

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
   (account: Account)
   (values: 'Values)
   (form: Form.Form<'Values, Msg<'Values>, IReactProperty>)
   (onSubmit: ParentOnSubmitHandler)
   =
   let state, dispatch =
      React.useElmish (init values, update account onSubmit, [||])

   let signalRContext = React.useContext SignalRConnectionProvider.context
   let errors = signalRContext.Errors

   React.useEffect (
      (fun () ->
         if
            state.FormModel.State = Form.View.State.Loading
            && errors.Length > 0
         then
            let err = errors.Head

            if account.AccountId = err.AccountId then
               dispatch <| Msg.ErrorReceivedViaSignalR err.Error),
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
