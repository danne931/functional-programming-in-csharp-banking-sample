[<RequireQualifiedAccess>]
module BillingCycleActor

open System
open Akka.Actor
open Akka.Hosting
open Akkling.Streams
open Akkling

open BillingStatement
open Bank.Account.Domain
open ActorUtil

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
   (getAccountRef: EntityRefGetter<AccountMessage>)
   =
   task {
      do!
         ActorUtil
            .readJournal(system)
            .CurrentPersistenceIds()
            .RunForeach(forwardToAccount getAccountRef, system.Materializer())

      return BillingCycleFinished
   }

let actorProps
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (broadcaster: AccountBroadcast)
   =
   let handler (ctx: Actor<BillingCycleMessage>) =
      function
      | BillingCycleFanout ->
         logInfo ctx "Start billing cycle"
         broadcaster.endBillingCycle () |> ignore

         fanOutBillingCycleMessage ctx.System getAccountRef |> Async.AwaitTask
         |!> retype ctx.Self
         |> ignored
      | BillingCycleFinished ->
         logInfo ctx "Billing cycle finished"
         ignored ()

   props <| actorOf2 handler

let get (system: ActorSystem) : IActorRef<BillingCycleMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.BillingCycleMarker>()

let initProps
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (broadcaster: AccountBroadcast)
   =
   actorProps getAccountRef broadcaster
