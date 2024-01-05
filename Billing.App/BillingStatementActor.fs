[<RequireQualifiedAccess>]
module BillingStatementActor

open Akka.Actor
open Akka.Hosting
open Akkling
open Akka.Streams
open Akkling.Streams
open FsToolkit.ErrorHandling

open BillingStatement
open ActorUtil
open Lib.Types
open Lib.BulkWriteStreamFlow

let get (system: ActorSystem) : IActorRef<BillingStatementMessage> =
   typed
   <| ActorRegistry.For(system).Get<ActorMetadata.BillingStatementMarker>()

// Collects billing statement records to insert in batches.
// Billing statements are persisted in a single transaction
// once the batch size limit is reached or after some duration
// in seconds from the initial BillingStatement message.
let start
   (system: ActorSystem)
   (persistence: BillingPersistence)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   : IActorRef<BillingStatementMessage>
   =
   let bulkWriteFlow, bulkWriteRef =
      initBulkWriteFlow<BillingStatement> system restartSettings chunking {
         persist =
            fun (statements: BillingStatement seq) ->
               statements |> List.ofSeq |> persistence.saveBillingStatements
         // Feed failed inserts back into the stream
         onRetry =
            fun (statements: BillingStatement seq) ->
               for bill in statements do
                  get system <! RegisterBillingStatement bill
         onPersistOk =
            fun _ response ->
               SystemLog.info system $"Saved billing statements {response}"
      }

   let flow =
      Flow.id<BillingStatementMessage, IActorRef<BillingStatementMessage>>
      // Filter for statements to be batched in the next stage.
      |> Flow.choose (fun msg ->
         match msg with
         | BillingStatementMessage.RegisterBillingStatement statement ->
            Some statement
         // Forward message to internal bulk write
         // actor without including message in next flow stage.
         | BillingStatementMessage.GetFailedWrites ->
            bulkWriteRef <<! BulkWriteMessage.GetFailedWrites
            None)
      |> Flow.via bulkWriteFlow

   Source.actorRef OverflowStrategy.Fail 1000
   |> Source.via flow
   |> Source.toMat Sink.ignore Keep.left
   |> Graph.run (system.Materializer())
