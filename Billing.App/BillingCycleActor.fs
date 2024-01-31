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
open Lib.Postgres

let getActiveAccountIds () =
   pgQuery<Guid>
      $"SELECT {AccountFields.entityId} FROM accounts WHERE status = '{string AccountStatus.Active}'"
      None
      AccountSqlReader.entityId

let private fanOutBillingCycleMessage
   (ctx: Actor<_>)
   (throttle: StreamThrottle)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   =
   task {
      let mat = ctx.System.Materializer()

      do!
         getActiveAccountIds ()
         |> Async.AwaitTask
         |> Source.ofAsync
         |> Source.throttle
               ThrottleMode.Shaping
               throttle.Burst
               throttle.Count
               throttle.Duration
         |> Source.choose (fun res ->
            match res with
            | Error e ->
               logError ctx $"Error fetching active account ids"
               None
            | Ok opt ->
               if opt.IsNone then
                  logError ctx "No active accounts."

               opt)
         |> Source.collect id
         |> Source.runForEach mat (fun accountId ->
            getAccountRef accountId <! AccountMessage.BillingCycle)

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

         fanOutBillingCycleMessage ctx throttle getAccountRef |> Async.AwaitTask
         |!> retype ctx.Self
         |> ignored
      | BillingCycleFinished ->
         logInfo ctx "Billing cycle finished"
         ignored ()

   props <| actorOf2 handler

let get (system: ActorSystem) : IActorRef<BillingCycleMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.BillingCycleMarker>()
