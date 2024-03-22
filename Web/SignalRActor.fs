[<RequireQualifiedAccess>]
module SignalRActor

open System
open Akka.Actor
open Akkling
open Microsoft.AspNetCore.SignalR

open ActorUtil
open Bank.Account.Domain
open Bank.Hubs

let actorProps (hub: IHubContext<AccountHub, IAccountClient>) =
   let handler (ctx: Actor<_>) = actor {
      registerSelfForPubSub ctx

      let! msg = ctx.Receive()

      match msg with
      | SignalRMessage.AccountEventPersisted msg ->
         hub.Clients
            .Group(string msg.NewState.EntityId)
            .AccountEventPersistenceConfirmation(Serialization.serialize msg)
         |> ignore
      | SignalRMessage.AccountEventValidationFail msg ->
         hub.Clients
            .Group(string msg.AccountId)
            .AccountEventValidationFail(Serialization.serialize msg)
         |> ignore
      | SignalRMessage.AccountEventPersistenceFail msg ->
         hub.Clients
            .Group(string msg.AccountId)
            .AccountEventPersistenceFail(Serialization.serialize msg)
         |> ignore
      | SignalRMessage.CircuitBreaker msg ->
         hub.Clients.All.CircuitBreakerMessage(Serialization.serialize msg)
         |> ignore
   }

   props handler

let start (system: ActorSystem) (hub: IHubContext<AccountHub, IAccountClient>) =
   spawn system ActorMetadata.signalR.Name <| actorProps hub
