module Lib.BulkWriteStreamFlow

open System
open Akka.Actor
open Akkling
open Akka.Streams.Dsl
open Akkling.Streams
open FsToolkit.ErrorHandling

open Lib.Types

type BulkWriteOptions<'T> = {
   RetryAfter: TimeSpan
   persist: 'T seq -> TaskResult<int list, Err>
   onRetry: 'T seq -> unit
   onPersistOk: 'T seq -> int list -> unit
}

type BulkWriteState<'T> = {
   FailedWrites: 'T seq
   IsRetryScheduled: bool
}

let private initState = {
   FailedWrites = Seq.empty
   IsRetryScheduled = false
}

type BulkWriteMessage<'T> =
   | GetFailedWrites
   | WriteFail of 'T seq
   | RetryFailedWrites

let private actorProps<'T> (opts: BulkWriteOptions<'T>) =
   let handler (ctx: Actor<BulkWriteMessage<'T>>) =
      let rec loop (state: BulkWriteState<'T>) =
         function
         | GetFailedWrites ->
            ctx.Sender() <! state.FailedWrites
            ignored ()
         | WriteFail statements ->
            if not state.IsRetryScheduled then
               ctx.Schedule opts.RetryAfter ctx.Self RetryFailedWrites |> ignore

            let newState = {
               FailedWrites = state.FailedWrites |> Seq.append statements
               IsRetryScheduled = true
            }

            become <| loop newState
         | RetryFailedWrites ->
            if Seq.isEmpty state.FailedWrites then
               ignored ()
            else
               opts.onRetry state.FailedWrites
               become <| loop initState

      loop initState

   props <| actorOf2 handler

// Collects records to bulk insert in batches.
// Records are persisted in a single transaction once the
// StreamChunking.Size limit is reached or after some duration.
// Flow processing is restarted with backoff upon persistence
// exception.
// Failed writes are stored & returned to the consumer upon
// a successful persistence or duration passed.
let initBulkWriteFlow<'T>
   (system: ActorSystem)
   (restartSettings: Akka.Streams.RestartSettings)
   (chunking: StreamChunking)
   (bulkWriteOpts: BulkWriteOptions<'T>)
   =
   let bulkWriteRef = spawnAnonymous system <| actorProps<'T> bulkWriteOpts

   let flow =
      Flow.id
      |> Flow.groupedWithin chunking.Size chunking.Duration
      |> Flow.taskMap 1 (fun (statements: 'T seq) -> task {
         let! res = bulkWriteOpts.persist statements

         return
            match res with
            | Ok insertResponse ->
               bulkWriteRef <! RetryFailedWrites
               bulkWriteOpts.onPersistOk statements insertResponse
               statements
            | Error e ->
               bulkWriteRef <! WriteFail statements
               failwith $"Bulk insert failed: {e}"
      })

   RestartFlow.OnFailuresWithBackoff((fun _ -> flow), restartSettings),
   bulkWriteRef
