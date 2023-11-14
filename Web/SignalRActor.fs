[<RequireQualifiedAccess>]
module SignalRActor

open Akka.Actor
open Akka.Cluster.Tools.PublishSubscribe
open Akkling
open Microsoft.AspNetCore.SignalR

open Lib.Types
open ActorUtil
open Bank.Account.Domain
open Bank.Hubs

let registerSelfForPubSub (ctx: Actor<_>) =
   DistributedPubSub.Get(ctx.System).Mediator.Tell(Put(untyped ctx.Self))

let actorProps (hub: IHubContext<AccountHub, IAccountClient>) =
   let handler (ctx: Actor<_>) = actor {
      registerSelfForPubSub ctx

      let! msg = ctx.Receive()

      match msg with
      | SignalRMessage.AccountEventPersisted(evt, account) ->
         hub.Clients
            .Group(string account.EntityId)
            .ReceiveMessage({| event = evt; newState = account |})
         |> ignore
      | SignalRMessage.AccountEventValidationFail msg ->
         // TODO: Change to Clients.Group & validation fail
         hub.Clients.All.ReceiveError(msg) |> ignore
      | SignalRMessage.AccountEventPersistenceFail msg ->
         // TODO: Change to Clients.Group & persistence fail
         hub.Clients.All.ReceiveError(msg) |> ignore
      | SignalRMessage.CircuitBreaker msg ->
         hub.Clients.All.ReceiveCircuitBreakerMessage(msg) |> ignore
      | SignalRMessage.EndBillingCycle ->
         hub.Clients.All.ReceiveBillingCycleEnd() |> ignore
   }

   props handler

let start (system: ActorSystem) (hub: IHubContext<AccountHub, IAccountClient>) =
   spawn system ActorMetadata.signalR.Name <| actorProps hub
