[<RequireQualifiedAccess>]
module AccountBroadcaster

open Akka.Actor
open Akka.Cluster.Tools.PublishSubscribe

open Bank.Account.Domain
open ActorUtil

let init (system: ActorSystem) : AccountBroadcast =
   let mediator = DistributedPubSub.Get(system).Mediator
   let signalRPath = ActorMetadata.signalR.Path.ToStringWithoutAddress()

   let circuitBreakerPath =
      ActorMetadata.circuitBreaker.SingletonPath.ToStringWithoutAddress()

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
