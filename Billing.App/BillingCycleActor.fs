[<RequireQualifiedAccess>]
module BillingCycleActor

open System
open Akka.Actor
open Akka.Hosting
open Akka.Streams
open Akkling.Streams
open Akkling

open BillingStatement
open Bank.Account.Domain
open ActorUtil
open Lib.Types

let private forwardToAccount
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (id: string)
   =
   let idOpt =
      try
         Guid id |> Some
      with _ ->
         None

   if idOpt.IsSome then
      getAccountRef idOpt.Value <! AccountMessage.BillingCycle

let private fanOutBillingCycleMessage
   system
   (throttle: StreamThrottle)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   =
   task {
      let source = (readJournal system).CurrentPersistenceIds()
      let mat = system.Materializer()

      do!
         source
         |> Source.throttle
               ThrottleMode.Shaping
               throttle.Burst
               throttle.Count
               throttle.Duration
         |> Source.runForEach mat (forwardToAccount getAccountRef)

      return BillingCycleFinished
   }

let actorProps
   (throttle: StreamThrottle)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (broadcaster: AccountBroadcast)
   =
   let handler (ctx: Actor<BillingCycleMessage>) =
      function
      | BillingCycleFanout ->
         logInfo ctx "Start billing cycle"
         broadcaster.endBillingCycle () |> ignore

         fanOutBillingCycleMessage ctx.System throttle getAccountRef
         |> Async.AwaitTask
         |!> retype ctx.Self
         |> ignored
      | BillingCycleFinished ->
         logInfo ctx "Billing cycle finished"
         ignored ()

   props <| actorOf2 handler

let get (system: ActorSystem) : IActorRef<BillingCycleMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.BillingCycleMarker>()
