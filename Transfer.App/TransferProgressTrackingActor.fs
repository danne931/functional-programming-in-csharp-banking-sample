[<RequireQualifiedAccess>]
module TransferProgressTrackingActor

open Akka.Actor
open Akka.Hosting
open Akka.Streams
open Akkling.Streams
open Akkling
open FsToolkit.ErrorHandling

open Lib.SharedTypes
open Bank.Transfer.Domain
open ActorUtil
open Lib.Types
open Lib.Postgres
open AccountSqlMapper
open DomesticTransferRecipientActor

let actorProps
   (system: ActorSystem)
   (getDomesticTransferActor: ActorSystem -> IActorRef<DomesticTransferMessage>)
   (getInProgressTransfers:
      unit -> Async<Result<Option<DomesticTransfer list>, Err>>)
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
                  DomesticTransferServiceAction.ProgressCheck,
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

// NOTE: Date Filter:
// Include in progress check if an hour has elapsed since transfer initiated.
//
// NOTE: Transfer Status Filter:
// Include in progress check if the initial transfer request has
// been acknowledged as received by the mock domestic transfer processor.
// This avoids transfer rejections due to a ProgressCheck being
// received by the mock domestic transfer processor before a TransferRequest
// in cases where a TransferRequest was rescheduled when the
// domestic transfer actor's circuit breaker was open.
let getProgressCheckReadyDomesticTransfers (lookbackMinutes: int) () = asyncResultOption {
   let reader (read: RowReader) =
      read.text "txns" |> Serialization.deserializeUnsafe<DomesticTransfer>

   let inProgressTransfersPath =
      AccountSqlMapper.table + "." + AccountFields.inProgressDomesticTransfers

   let inProgressTransfersCountPath =
      AccountSqlMapper.table
      + "."
      + AccountFields.inProgressDomesticTransfersCount

   let! transfers =
      pgQuery<DomesticTransfer>
         $"""
         SELECT txns
         FROM
            {AccountSqlMapper.table},
            jsonb_array_elements({inProgressTransfersPath}) as txns
         WHERE
            {inProgressTransfersCountPath} > 0
            AND (txns ->> 'ScheduledDate')::timestamptz < current_timestamp - '{lookbackMinutes} minutes'::interval
            AND (
               (txns #>> '{{Status}}') = 'Outgoing'
               OR (txns #>> '{{Status,0}}') = 'InProgress'
            )
         """
         None
         reader

   return transfers
}

let initProps
   (system: ActorSystem)
   (getDomesticTransferActor: ActorSystem -> IActorRef<DomesticTransferMessage>)
   =
   actorProps
      system
      getDomesticTransferActor
      (getProgressCheckReadyDomesticTransfers
         EnvTransfer.config.TransferProgressLookbackMinutes)
      EnvTransfer.config.TransferProgressTrackingThrottle
