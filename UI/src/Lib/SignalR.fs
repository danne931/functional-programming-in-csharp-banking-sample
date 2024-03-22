module SignalR

open Fable.Core
open Fable.Core.JS
open Fable.Core.JsInterop
open System
open FsToolkit.ErrorHandling

open Lib.SharedTypes

type IPromiseConnection =
   abstract start: unit -> Promise<unit>
   abstract stop: unit -> Promise<unit>

   [<Emit("$0.invoke('AddToConnectionGroup', $1)")>]
   abstract addAccountToConnectionGroup: string -> Promise<obj>

   [<Emit("$0.invoke('RemoveFromConnectionGroup', $1)")>]
   abstract removeAccountFromConnectionGroup: string -> Promise<obj>

   [<Emit("$0.on($1, $2)")>]
   abstract on: string * (string -> unit) -> unit

type Connection(conn: IPromiseConnection) =
   member x.start() =
      conn.start ()
      |> AsyncUtil.promiseToAsyncResult
      |> AsyncResult.mapError Err.SignalRError

   member x.stop() =
      conn.stop ()
      |> AsyncUtil.promiseToAsyncResult
      |> AsyncResult.mapError Err.SignalRError

   member x.addAccountToConnectionGroup(accountId: Guid) =
      conn.addAccountToConnectionGroup (string accountId)
      |> AsyncUtil.promiseToAsyncResult
      |> AsyncResult.mapError Err.SignalRError

   member x.removeAccountFromConnectionGroup(accountId: Guid) =
      conn.removeAccountFromConnectionGroup (string accountId)
      |> AsyncUtil.promiseToAsyncResult
      |> AsyncResult.mapError Err.SignalRError

   member x.on(eventName: string, eventHandler: string -> unit) =
      conn.on (eventName, eventHandler)

type private IHubConnectionBuilder =
   abstract withUrl: string -> IHubConnectionBuilder
   abstract withAutomaticReconnect: unit -> IHubConnectionBuilder
   abstract build: unit -> IPromiseConnection

type private IHubBuilderImport =
   [<EmitConstructor>]
   abstract create: unit -> IHubConnectionBuilder

let private hubConnectionBuilder: IHubBuilderImport =
   import "HubConnectionBuilder" "@microsoft/signalr"

let buildConnection (url: string) : Connection =
   let conn =
      hubConnectionBuilder
         .create()
         .withUrl(url)
         .withAutomaticReconnect()
         .build ()

   Connection conn
