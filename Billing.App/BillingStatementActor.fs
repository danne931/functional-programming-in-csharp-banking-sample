[<RequireQualifiedAccess>]
module BillingStatementActor

open Akka.Actor
open Akka.Hosting
open Akkling
open Akka.Streams
open Akka.Streams.Dsl
open Akkling.Streams
open FSharp.Control
open FsToolkit.ErrorHandling

open BillingStatement
open ActorUtil
open Lib.Types

let get (system: ActorSystem) : IActorRef<BillingStatementMessage> =
   typed
   <| ActorRegistry.For(system).Get<ActorMetadata.BillingStatementMarker>()

type BulkWriteState = { FailedWrites: BillingStatement seq }
let private initState = { FailedWrites = Seq.empty }

let actorProps (billingRef: unit -> IActorRef<BillingStatementMessage>) =
   let handler (ctx: Actor<BillingStatementMessage>) =
      let rec loop (state: BulkWriteState) =
         function
         | GetFailedWrites ->
            ctx.Sender() <! state
            ignored ()
         | WriteFail statements ->
            let newState = {
               FailedWrites = state.FailedWrites |> Seq.append statements
            }

            become <| loop newState
         | WriteSuccess ->
            if Seq.isEmpty state.FailedWrites then
               ignored ()
            else
               for bill in state.FailedWrites do
                  billingRef () <! RegisterBillingStatement bill

               become <| loop initState
         | msg -> unhandled msg

      loop initState

   props <| actorOf2 handler

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
   let errorHandler = spawnAnonymous system (actorProps (fun () -> get system))

   let flow =
      Flow.id<BillingStatementMessage, IActorRef<BillingStatementMessage>>
      // Filter for statements to be batched in the next stage.
      |> Flow.choose (fun msg ->
         match msg with
         | RegisterBillingStatement statement -> Some statement
         | msg ->
            // Forward other messages
            // (ex: BillingStatementMessage.GetFailedWrites) to internal
            // error handler actor & don't include message in next stage.
            errorHandler <<! msg
            None)
      |> Flow.groupedWithin chunking.Size chunking.Duration
      |> Flow.taskMap 1 (fun (statements: BillingStatement seq) -> task {
         printfn "%A" statements

         let! res =
            statements |> List.ofSeq |> persistence.saveBillingStatements

         return
            match res with
            | Ok o ->
               SystemLog.info system $"Saved billing statements {o}"
               errorHandler <! WriteSuccess
               statements
            | Error e ->
               errorHandler <! WriteFail statements
               failwith $"Error saving billing statements {e}"
      })

   let flow =
      RestartFlow.OnFailuresWithBackoff((fun _ -> flow), restartSettings)

   Source.actorRef OverflowStrategy.Fail 1000
   |> Source.via flow
   |> Source.toMat Sink.ignore Keep.left
   |> Graph.run (system.Materializer())
