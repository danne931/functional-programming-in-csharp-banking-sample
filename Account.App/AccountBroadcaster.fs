[<RequireQualifiedAccess>]
module AccountBroadcaster

open Akkling
open Akka.Actor
open Akka.Cluster.Tools.PublishSubscribe

open Bank.Account.Domain
open ActorUtil
open AccountLoadTestTypes

let init (system: ActorSystem) : AccountBroadcast =
   let mediator = DistributedPubSub.Get(system).Mediator
   let signalRPath = ActorMetadata.signalR.Path.ToStringWithoutAddress()

   let loadTestPath =
      ActorMetadata.accountLoadTest.SingletonPath.ToStringWithoutAddress()

   {
      accountEventPersisted =
         fun event account ->
            mediator.Tell(
               Send(
                  signalRPath,
                  SignalRMessage.AccountEventPersisted(event, account)
               )
            )

            if Env.allowLiveLoadTest then
               mediator.Tell(
                  Send(
                     loadTestPath,
                     AccountLoadTestMessage.AccountEventPersisted {
                        AccountId = account.EntityId
                        Event = event
                        AccountBalance = account.Balance
                     }
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
            let circuitBreakerAref = CircuitBreakerActor.get system
            circuitBreakerAref <! CircuitBreakerMessage.CircuitBreaker msg

            mediator.Tell(Send(signalRPath, SignalRMessage.CircuitBreaker msg))
      endBillingCycle =
         fun () ->
            mediator.Tell(Send(signalRPath, SignalRMessage.EndBillingCycle))
   }
