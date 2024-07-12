module SignalRConnectionProvider

open Feliz
open Feliz.UseElmish
open Elmish

let context =
   React.createContext<SignalR.Connection option> (
      name = "SignalRConnectionContext",
      defaultValue = None
   )

type Msg = ConnectToSignalR of AsyncOperationStatus<SignalR.Connection>

let init () =
   None, Cmd.ofMsg (ConnectToSignalR Started)

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
   | ConnectToSignalR(Finished connection) -> Some connection, Cmd.none

[<ReactComponent>]
let SignalRConnectionProvider (child: Fable.React.ReactElement) =
   let connection, _ = React.useElmish (init, update, [||])

   React.useEffect (
      fun () ->
         React.createDisposable (fun _ ->
            match connection with
            | Some conn -> conn.stop () |> Async.Ignore |> Async.StartImmediate
            | None -> ())
      , [| box connection |]
   )

   React.contextProvider (context, connection, child)
