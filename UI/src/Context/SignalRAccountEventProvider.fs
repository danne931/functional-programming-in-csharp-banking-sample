module SignalRAccountEventProvider

open Feliz
open Feliz.UseElmish
open Elmish
open FsToolkit.ErrorHandling

open Bank.Account.Domain
open Lib.SharedTypes

type SignalRAccountEventContext = {
   Subscribers: Map<string, (AccountEventPersistedConfirmation -> unit)>
   RealtimeEvents: AccountEvent list
   Errors: AccountEventRejected list
   CurrentAccountId: AccountId option
   QueuedAccountId: AccountId option
}

let private initState = {
   Subscribers = Map.empty
   RealtimeEvents = []
   Errors = []
   CurrentAccountId = None
   QueuedAccountId = None
}

let private addAccountToConnection
   (connection: SignalR.Connection)
   (existingAccountId: AccountId option)
   (accountIdToAdd: AccountId)
   : Async<Result<AccountId, Err>>
   =
   asyncResult {
      if
         existingAccountId.IsSome && existingAccountId <> Some accountIdToAdd
      then
         let! _ =
            connection.removeAccountFromConnectionGroup existingAccountId.Value

         ()

      let! _ = connection.addAccountToConnectionGroup accountIdToAdd

      return accountIdToAdd
   }

let context =
   React.createContext<SignalRAccountEventContext> (
      name = "SignalRAccountEventContext",
      defaultValue = initState
   )

type Msg =
   | AccountEventPersisted of AccountEventPersistedConfirmation
   | AddAccountEventSubscriber of
      componentName: string *
      AccountId *
      (AccountEventPersistedConfirmation -> unit)
   | RemoveAccountEventSubscriber of componentName: string
   | QueueAccountConnectionStart of AccountId
   | AddAccountToConnection of
      SignalR.Connection *
      AccountId *
      AsyncOperationStatus<Result<AccountId, Err>>
   | AccountErrorReceived of AccountEventRejected

let dispatchContext =
   React.createContext<Msg -> unit> (
      name = "SignalRAccountEventDispatchContext",
      defaultValue = ignore
   )

let init () = initState, Cmd.none

let update msg state =
   match msg with
   | QueueAccountConnectionStart accountId ->
      {
         state with
            QueuedAccountId = Some accountId
      },
      Cmd.none
   | AddAccountToConnection(conn, accountId, Started) ->
      let addToConn = async {
         let! res = addAccountToConnection conn state.CurrentAccountId accountId
         return Msg.AddAccountToConnection(conn, accountId, Finished res)
      }

      {
         state with
            CurrentAccountId = Some accountId
            QueuedAccountId = None
            RealtimeEvents = []
      },
      Cmd.fromAsync addToConn
   | AddAccountToConnection(_, _, Finished(Error err)) ->
      { state with CurrentAccountId = None }, Cmd.none
   | AddAccountToConnection(_, _, Finished(Ok r)) -> state, Cmd.none
   | AddAccountEventSubscriber(componentName, accountId, callback) ->
      {
         state with
            Subscribers =
               match state.CurrentAccountId, state.QueuedAccountId with
               | Some existingId, _ when existingId = accountId ->
                  Map.add componentName callback state.Subscribers
               | None, Some queuedId when queuedId = accountId ->
                  Map.add componentName callback state.Subscribers
               | _ ->
                  // Reset subscribers when AccountId changes
                  Map[componentName, callback]
      },
      Cmd.none
   | RemoveAccountEventSubscriber componentName ->
      {
         state with
            Subscribers = state.Subscribers.Remove componentName
      },
      Cmd.none
   | AccountEventPersisted conf ->
      for subscriberCallback in state.Subscribers.Values do
         subscriberCallback conf

      {
         state with
            RealtimeEvents = conf.EventPersisted :: state.RealtimeEvents
      },
      Cmd.none
   | AccountErrorReceived(msg) ->
      {
         state with
            Errors = msg :: state.Errors
      },
      Cmd.none

[<ReactComponent>]
let SignalRAccountEventProvider (child: Fable.React.ReactElement) =
   let state, dispatch = React.useElmish (init, update, [||])
   let connection = React.useContext SignalRConnectionProvider.context

   React.useEffect (
      fun () ->
         match connection with
         | Some conn ->
            let onSerializedAccountEventError (errMsg: string) =
               let deseri =
                  Serialization.deserialize<AccountEventRejected> errMsg

               match deseri with
               | Error seriErr -> Log.error (string seriErr)
               | Ok msg -> msg |> Msg.AccountErrorReceived |> dispatch

            conn.on (
               "AccountEventPersistenceFail",
               onSerializedAccountEventError
            )

            conn.on (
               "AccountEventValidationFail",
               onSerializedAccountEventError
            )

            let deseri =
               Serialization.deserialize<AccountEventPersistedConfirmation>

            conn.on (
               "AccountEventPersistenceConfirmation",
               fun (msg: string) ->
                  match deseri msg with
                  | Error err -> Log.error (string err)
                  | Ok msg -> dispatch <| Msg.AccountEventPersisted msg
            )
         | _ -> ()
      , [| box connection |]
   )

   React.useEffect (
      fun () ->
         React.createDisposable (fun () ->
            match connection, state.CurrentAccountId with
            | Some conn, Some accountId ->
               conn.removeAccountFromConnectionGroup accountId
               |> Async.Ignore
               |> Async.StartImmediate
            | _ -> ())
      , [| box connection; box (string state.CurrentAccountId) |]
   )

   React.contextProvider (
      context,
      state,
      React.contextProvider (dispatchContext, dispatch, child)
   )

type AccountEventSubscription = {
   ComponentName: string
   AccountId: AccountId option
   OnReceive: AccountEventPersistedConfirmation -> unit
}

// Custom hook to subscribe to persisted account events received
// via SignalR.
// The subscription is removed when the consuming component unmounts.
let useAccountEventSubscription (sub: AccountEventSubscription) =
   let state = React.useContext context
   let dispatch = React.useContext dispatchContext
   let connection = React.useContext SignalRConnectionProvider.context

   let accountIdOpt = sub.AccountId

   React.useEffect (
      fun () ->
         match accountIdOpt with
         | Some accountId ->
            (sub.ComponentName, accountId, sub.OnReceive)
            |> Msg.AddAccountEventSubscriber
            |> dispatch
         | _ -> ()

         React.createDisposable (fun _ ->
            sub.ComponentName |> Msg.RemoveAccountEventSubscriber |> dispatch)
      , [| box (string accountIdOpt) |]
   )

   React.useEffect (
      fun () ->
         match connection, accountIdOpt, state.CurrentAccountId with
         | Some conn, Some id, None ->
            dispatch <| Msg.AddAccountToConnection(conn, id, Started)
         | Some conn, Some id, Some existingId when id <> existingId ->
            dispatch <| Msg.AddAccountToConnection(conn, id, Started)
         | None, Some id, _ when (Some id) <> state.QueuedAccountId ->
            dispatch <| Msg.QueueAccountConnectionStart id
         | _ -> ()
      , [| box (string accountIdOpt); box connection |]
   )
