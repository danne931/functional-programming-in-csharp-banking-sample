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
      | SignalRMessage.AccountEventPersisted(evt, account) ->
         hub.Clients
            .Group(string account.EntityId)
            .ReceiveMessage({| event = evt; newState = account |})
         |> ignore
      | SignalRMessage.AccountEventValidationFail(accountId: Guid, msg: string) ->
         hub.Clients.Group(string accountId).ReceiveError(msg) |> ignore
      | SignalRMessage.AccountEventPersistenceFail(accountId: Guid, msg: string) ->
         hub.Clients.Group(string accountId).ReceiveError(msg) |> ignore
      | SignalRMessage.CircuitBreaker msg ->
         hub.Clients.All.ReceiveCircuitBreakerMessage(msg) |> ignore
      | SignalRMessage.EndBillingCycle ->
         hub.Clients.All.ReceiveBillingCycleEnd() |> ignore
   }

   props handler

let start (system: ActorSystem) (hub: IHubContext<AccountHub, IAccountClient>) =
   spawn system ActorMetadata.signalR.Name <| actorProps hub
