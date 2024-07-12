[<RequireQualifiedAccess>]
module SignalRActor

open Akka.Actor
open Akkling
open Microsoft.AspNetCore.SignalR

open ActorUtil
open Bank.Account.Domain
open Bank.Hubs
open Lib.SharedTypes

[<RequireQualifiedAccess>]
type Msg =
   | AccountEventPersisted of AccountEventPersistedConfirmation
   | AccountEventValidationFail of AccountEventRejected
   | AccountEventPersistenceFail of AccountEventRejected
   | CircuitBreaker of CircuitBreakerEvent

let actorProps (hub: IHubContext<AccountHub, IAccountClient>) =
   let handler (ctx: Actor<_>) = actor {
      registerSelfForPubSub ctx

      let! msg = ctx.Receive()

      match msg with
      | Msg.AccountEventPersisted msg ->
         hub.Clients
            .Group(string msg.Account.AccountId)
            .AccountEventPersistenceConfirmation(Serialization.serialize msg)
         |> ignore
      | Msg.AccountEventValidationFail msg ->
         hub.Clients
            .Group(string msg.AccountId)
            .AccountEventValidationFail(Serialization.serialize msg)
         |> ignore
      | Msg.AccountEventPersistenceFail msg ->
         hub.Clients
            .Group(string msg.AccountId)
            .AccountEventPersistenceFail(Serialization.serialize msg)
         |> ignore
      | Msg.CircuitBreaker msg ->
         hub.Clients.All.CircuitBreakerMessage(Serialization.serialize msg)
         |> ignore
   }

   props handler

let start (system: ActorSystem) (hub: IHubContext<AccountHub, IAccountClient>) =
   spawn system ActorMetadata.signalR.Name <| actorProps hub
