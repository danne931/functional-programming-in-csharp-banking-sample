[<RequireQualifiedAccess>]
module BillingStatementActor

open System
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

// NOTE:
// --BillingStatementActor--
// Collects billing statement records to insert in batches.
// Billing statements are persisted in a single transaction
// once the batch size limit is reached or after some duration
// in seconds from the initial BillingStatement message.

let get (system: ActorSystem) : IActorRef<BillingStatementMessage> =
   typed
   <| ActorRegistry.For(system).Get<ActorMetadata.BillingStatementMarker>()

let initQueueSource
   (system: ActorSystem)
   (persistence: BillingPersistence)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryAfter: TimeSpan)
   (billingStatementActorRef: IActorRef<obj>)
   =
   let flow, bulkWriteRef =
      initBulkWriteFlow<BillingStatement> system restartSettings chunking {
         RetryAfter = retryAfter
         persist =
            fun (statements: BillingStatement seq) ->
               statements |> List.ofSeq |> persistence.saveBillingStatements
         // Feed failed inserts back into the stream
         onRetry =
            fun (statements: BillingStatement seq) ->
               for bill in statements do
                  billingStatementActorRef <! RegisterBillingStatement bill
         onPersistOk =
            fun _ response ->
               SystemLog.info system $"Saved billing statements {response}"
      }

   bulkWriteRef,
   Source.queue OverflowStrategy.Backpressure 1000 |> Source.via flow

let actorProps
   (system: ActorSystem)
   (persistence: BillingPersistence)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryFailedPersistenceAfter: TimeSpan)
   =
   let rec init (ctx: Actor<obj>) = actor {
      let! msg = ctx.Receive()

      match msg with
      | LifecycleEvent e ->
         match e with
         | PreStart ->
            logInfo ctx "Prestart - Init BillingStatementActor Queue Source"

            let (bulkWriteRef, queueSource) =
               initQueueSource
                  system
                  persistence
                  chunking
                  restartSettings
                  retryFailedPersistenceAfter
                  ctx.Self

            let queue =
               queueSource
               |> Source.toMat Sink.ignore Keep.left
               |> Graph.run (system.Materializer())

            return! processing ctx queue bulkWriteRef
         | _ -> return ignored ()
      | msg ->
         logError ctx $"Unknown msg {msg}"
         return unhandled ()
   }

   and processing
      (ctx: Actor<obj>)
      (queue: ISourceQueueWithComplete<BillingStatement>)
      (bulkWriteRef: IActorRef<BulkWriteMessage<BillingStatement>>)
      =
      actor {
         let! msg = ctx.Receive()

         match msg with
         | :? BillingStatementMessage as msg ->
            match msg with
            | RegisterBillingStatement statement ->
               let! result = queueOffer<BillingStatement> queue statement

               return
                  match result with
                  | Ok effect -> effect
                  | Error errMsg -> failwith errMsg
            | BillingStatementMessage.GetFailedWrites ->
               let! failedWrites =
                  bulkWriteRef <? BulkWriteMessage.GetFailedWrites

               ctx.Sender() <! failedWrites
         | LifecycleEvent e ->
            match e with
            | PostStop ->
               queue.Complete()
               return! init ctx
            | _ -> return ignored ()
         | msg ->
            logError ctx $"Unknown msg {msg}"
            return unhandled ()
      }

   props init

let start
   (system: ActorSystem)
   (persistence: BillingPersistence)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryFailedPersistenceAfter: TimeSpan)
   : IActorRef<BillingStatementMessage>
   =
   spawn system ActorMetadata.billingStatement.Name
   <| actorProps
         system
         persistence
         chunking
         restartSettings
         retryFailedPersistenceAfter
   |> retype
