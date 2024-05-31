module SignalRConnectionProvider

open Feliz
open Feliz.UseElmish
open Elmish

open AsyncUtil
open Bank.Account.Domain

type SignalRContext = {
   Connection: SignalR.Connection option
   Errors: AccountEventRejected list
}

let context =
   React.createContext<SignalRContext> (
      name = "SignalRContext",
      defaultValue = { Connection = None; Errors = [] }
   )

type State = {
   SignalRConnection: SignalR.Connection option
   SignalRErrors: AccountEventRejected list
}

type Msg =
   | ConnectToSignalR of AsyncOperationStatus<SignalR.Connection>
   | ErrorReceived of AccountEventRejected

let init () =
   {
      SignalRConnection = None
      SignalRErrors = []
   },
   Cmd.ofMsg (ConnectToSignalR Started)

let update msg state =
   match msg with
   | ConnectToSignalR Started ->
      let connection = SignalR.buildConnection "/accountHub"

      let connect = async {
         match! connection.start () with
         | Ok _ -> return Msg.ConnectToSignalR(Finished connection)
         | Error e ->
            Log.error $"Error connecting to SignalR. {e}.  Trying again."
            do! Async.Sleep 1500
            return Msg.ConnectToSignalR Started
      }

      state, Cmd.fromAsync connect
   | ConnectToSignalR(Finished connection) ->
      {
         state with
            SignalRConnection = Some connection
      },
      Cmd.none
   | ErrorReceived(msg) ->
      {
         state with
            SignalRErrors = msg :: state.SignalRErrors
      },
      Cmd.none

[<ReactComponent>]
let SignalRConnectionProvider (child: Fable.React.ReactElement) =
   let state, dispatch = React.useElmish (init, update, [||])

   React.useEffect (
      fun () ->
         state.SignalRConnection
         |> Option.map (fun conn ->
            let onSerializedAccountEventError (errMsg: string) =
               let deseri =
                  Serialization.deserialize<AccountEventRejected> errMsg

               match deseri with
               | Error seriErr -> Log.error (string seriErr)
               | Ok msg -> msg |> Msg.ErrorReceived |> dispatch

            conn.on (
               "AccountEventPersistenceFail",
               onSerializedAccountEventError
            )

            conn.on (
               "AccountEventValidationFail",
               onSerializedAccountEventError
            ))
         |> ignore

         React.createDisposable (fun _ ->
            match state.SignalRConnection with
            | Some conn -> conn.stop () |> ignore
            | None -> ())
      , [| box state.SignalRConnection |]
   )

   React.contextProvider (
      context,
      {
         Connection = state.SignalRConnection
         Errors = state.SignalRErrors
      },
      child
   )
