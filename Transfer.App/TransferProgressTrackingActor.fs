[<RequireQualifiedAccess>]
module TransferProgressTrackingActor

open Akka.Actor
open Akka.Hosting
open Akka.Streams
open Akkling.Streams
open Akkling

open Bank.Transfer.Domain
open ActorUtil
open Lib.Types

let actorProps
   (system: ActorSystem)
   (getDomesticTransferActor: ActorSystem -> IActorRef<DomesticTransferMessage>)
   (getInProgressTransfers: GetInProgressTransfers)
   (throttle: StreamThrottle)
   =
   let mat = system.Materializer()

   let handler (ctx: Actor<TransferProgressTrackingMessage>) = actor {
      let! _ = ctx.Receive()

      logInfo ctx "Running progress checks over in-progress transfers."
      let domesticTransferActorRef = getDomesticTransferActor system

      do!
         getInProgressTransfers ()
         |> Source.ofAsync
         |> Source.choose (fun res ->
            match res with
            | Error e ->
               logError ctx $"Error fetching in progress transfers {e}"
               None
            | Ok opt ->
               if opt.IsNone then
                  logWarning ctx "No in progress transfers."

               opt)
         |> Source.collect id
         |> Source.throttle
               ThrottleMode.Shaping
               throttle.Burst
               throttle.Count
               throttle.Duration
         |> Source.runForEach mat (fun transferInProgress ->
            let msg =
               DomesticTransferMessage.TransferRequest(
                  TransferServiceAction.ProgressCheck,
                  transferInProgress
               )

            domesticTransferActorRef <! msg)

      logInfo ctx "Finished running progress checks over in-progress transfers."

      return ignored ()
   }

   props handler

let get (system: ActorSystem) : IActorRef<TransferProgressTrackingMessage> =
   typed
   <| ActorRegistry
      .For(system)
      .Get<ActorMetadata.TransferProgressTrackingMarker>()
