module Bank.Account.Forms.FormContainer

open Feliz
open Feliz.UseElmish
open Elmish
open Fable.Form.Simple

open Fable.Form.Simple.Pico
open Lib.SharedTypes
open Bank.Account.Domain
open UIDomain.Account

type ParentOnSubmitHandler = AccountCommandReceipt -> unit

type State<'Values> = { FormModel: Form.View.Model<'Values> }

type Msg<'Values> =
   | FormChanged of Form.View.Model<'Values>
   | GetAccountAndSubmit of AccountId * AccountCommand
   | Submit of
      Account *
      AccountCommand *
      AsyncOperationStatus<Result<AccountCommandReceipt, Err>>
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
   | GetAccountAndSubmit(accountId, command) ->
      let getAccount = async {
         let! account = AccountService.getAccount accountId

         match account with
         | Ok(Some account) -> return Msg.Submit(account, command, Started)
         | Ok None ->
            let err =
               AccountStateTransitionError.AccountNotActive
               |> Err.AccountStateTransitionError
               |> Error

            return Msg.Submit(Account.empty, command, Finished err)
         | Error err ->
            return Msg.Submit(Account.empty, command, Finished(Error err))
      }

      state, Cmd.fromAsync getAccount
   | Submit(account, command, Started) ->
      let submit = async {
         let! res = AccountService.submitCommand account command
         return Submit(account, command, Finished res)
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
let AccountFormContainer
   (values: 'Values)
   (form: Form.Form<'Values, Msg<'Values>, IReactProperty>)
   (onSubmit: ParentOnSubmitHandler)
   =
   let state, dispatch = React.useElmish (init values, update onSubmit, [||])

   (*
   let signalRContext = React.useContext SignalRConnectionProvider.context
   let errors = signalRContext.Errors.Account

   React.useEffect (
      (fun () ->
         if
            state.FormModel.State = Form.View.State.Loading
            && errors.Length > 0
         then
            dispatch <| Msg.ErrorReceivedViaSignalR errors.Head.Error),
      [| box errors.Length |]
   )
   *)

   Form.View.asHtml
      {
         Dispatch = dispatch
         OnChange = FormChanged
         Action = Form.View.Action.SubmitOnly "Submit"
         Validation = Form.View.ValidateOnSubmit
      }
      form
      state.FormModel
