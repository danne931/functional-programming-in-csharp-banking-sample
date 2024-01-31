[<RequireQualifiedAccess>]
module TransferProgressTrackingActor

open System
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
   (transferLookbackMinutes: int)
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
         |> Source.filter (fun txn ->
            // Only do progress checks if the initial transfer request has
            // been acknowledged as received by the mock 3rd party bank.
            // This avoids transfer rejections due to a ProgressCheck being
            // received by the mock 3rd party bank before a TransferRequest.
            let statusFilter =
               txn.Status <> TransferProgress.Outgoing
               && txn.Status <> TransferProgress.Complete

            let dateFilter =
               if Env.isDev then
                  true
               else
                  // Only do progress check if an hour (for example) has elapsed
                  // since transfer initiated.
                  txn.Date < DateTime.UtcNow.AddMinutes
                     -transferLookbackMinutes

            statusFilter && dateFilter)
         |> Source.throttle
               ThrottleMode.Shaping
               throttle.Burst
               throttle.Count
               throttle.Duration
         |> Source.runForEach mat (fun transferInProgress ->
            match transferInProgress.Recipient.AccountEnvironment with
            | RecipientAccountEnvironment.Domestic ->
               let msg =
                  DomesticTransferMessage.TransferRequest(
                     TransferServiceAction.ProgressCheck,
                     transferInProgress
                  )

               domesticTransferActorRef <! msg
            | RecipientAccountEnvironment.Internal ->
               logWarning
                  ctx
                  $"Internal transfer still in progress {transferInProgress}")

      logInfo ctx "Finished running progress checks over in-progress transfers."

      return ignored ()
   }

   props handler

let get (system: ActorSystem) : IActorRef<TransferProgressTrackingMessage> =
   typed
   <| ActorRegistry
      .For(system)
      .Get<ActorMetadata.TransferProgressTrackingMarker>()
