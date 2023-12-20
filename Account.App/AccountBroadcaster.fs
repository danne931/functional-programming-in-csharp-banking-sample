[<RequireQualifiedAccess>]
module AccountBroadcaster

open Akka.Actor
open Akka.Cluster.Tools.PublishSubscribe

open Bank.Account.Domain
open ActorUtil

let init (system: ActorSystem) : AccountBroadcast =
   let mediator = DistributedPubSub.Get(system).Mediator
   let signalRPath = $"/user/{ActorMetadata.signalR.Name}"

   {
      accountEventPersisted =
         fun event account ->
            mediator.Tell(
               Send(
                  signalRPath,
                  SignalRMessage.AccountEventPersisted(event, account)
               )
            )
      accountEventValidationFail =
         fun accountId errMsg ->
            mediator.Tell(
               Send(
                  signalRPath,
                  SignalRMessage.AccountEventValidationFail(accountId, errMsg)
               )
            )
      accountEventPersistenceFail =
         fun accountId errMsg ->
            mediator.Tell(
               Send(
                  signalRPath,
                  SignalRMessage.AccountEventPersistenceFail(accountId, errMsg)
               )
            )
      circuitBreaker =
         fun msg ->
            let circuitBreakerPath =
               $"/user/{ActorMetadata.circuitBreaker.Name}/{ActorMetadata.circuitBreaker.Name}"

            mediator.Tell(
               Send(
                  circuitBreakerPath,
                  CircuitBreakerMessage.CircuitBreaker msg
               )
            )

            mediator.Tell(Send(signalRPath, SignalRMessage.CircuitBreaker msg))
      endBillingCycle =
         fun () ->
            mediator.Tell(Send(signalRPath, SignalRMessage.EndBillingCycle))
   }
