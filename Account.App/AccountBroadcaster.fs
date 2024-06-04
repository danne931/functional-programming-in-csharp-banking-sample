[<RequireQualifiedAccess>]
module AccountBroadcaster

open Akkling
open Akka.Actor
open Akka.Cluster.Tools.PublishSubscribe
open System

open Lib.SharedTypes
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
                  SignalRMessage.AccountEventPersisted(
                     {
                        Date = DateTime.UtcNow
                        EventPersisted = event
                        NewState = account
                     }
                  )
               )
            )

            if Env.allowLiveLoadTest then
               mediator.Tell(
                  Send(
                     loadTestPath,
                     AccountLoadTestMessage.AccountEventPersisted {
                        AccountId = account.AccountId
                        Event = event
                        AccountBalance = account.Balance
                     }
                  )
               )
      accountEventValidationFail =
         fun accountId err ->
            mediator.Tell(
               Send(
                  signalRPath,
                  SignalRMessage.AccountEventValidationFail {
                     AccountId = accountId
                     Error = err
                     Date = DateTime.UtcNow
                  }
               )
            )
      accountEventPersistenceFail =
         fun accountId err ->
            mediator.Tell(
               Send(
                  signalRPath,
                  SignalRMessage.AccountEventPersistenceFail {
                     AccountId = accountId
                     Error = err
                     Date = DateTime.UtcNow
                  }
               )
            )
      circuitBreaker =
         fun msg ->
            let circuitBreakerAref = CircuitBreakerActor.get system
            circuitBreakerAref <! CircuitBreakerMessage.CircuitBreaker msg

            mediator.Tell(Send(signalRPath, SignalRMessage.CircuitBreaker msg))
   }
