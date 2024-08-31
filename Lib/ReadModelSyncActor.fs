module Lib.ReadModelSyncActor

open System
open System.Threading.Tasks
open Akka.Actor
open Akka.Persistence
open Akka.Streams
open Akka.Streams.Dsl
open Akkling
open Akkling.Persistence
open Akkling.Streams
open FsToolkit.ErrorHandling

open Lib.SharedTypes
open Lib.Types
open ActorUtil
open Lib.BulkWriteStreamFlow

type ReadModelUpsert<'TAggregate, 'TEvent> =
   'TAggregate list * 'TEvent list -> Result<int list, Err> Task

type ReadModelSyncConfig<'TAggregate, 'TEvent> = {
   EventJournalTag: string
   GetAggregateIdFromEvent: 'TEvent -> Guid
   GetAggregate: Guid -> 'TAggregate option Task
   Chunking: StreamChunking
   RestartSettings: Akka.Streams.RestartSettings
   RetryPersistenceAfter: TimeSpan
   UpsertReadModels: ReadModelUpsert<'TAggregate, 'TEvent>
}

type State = {
   Offset: Akka.Persistence.Query.Sequence
}

type Message = SaveOffset of Akka.Persistence.Query.Sequence

let initFailedWritesSource
   (system: ActorSystem)
   : Source<'TEvent list, Akka.NotUsed> * IActorRef<'TEvent list>
   =
   let failedWritesSource = Source.actorRef OverflowStrategy.DropHead 1000
   let preMat = failedWritesSource.PreMaterialize system

   preMat.Last(), preMat.Head()

let initReadJournalSource
   (mailbox: Eventsourced<obj>)
   (state: State)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (eventJournalTag: string)
   : Source<'TEvent list, Akka.NotUsed>
   =
   let source =
      (readJournal mailbox.System).EventsByTag(eventJournalTag, state.Offset)
      |> Source.groupedWithin chunking.Size chunking.Duration
      |> Source.map (fun evtSeq ->
         let evts: 'TEvent list =
            evtSeq |> Seq.map (fun env -> unbox env.Event) |> List.ofSeq

         let offset = (Seq.last evtSeq).Offset

         match offset with
         | :? Query.Sequence as offset -> mailbox.Self <! SaveOffset offset
         | _ -> ()

         evts)

   RestartSource.OnFailuresWithBackoff((fun _ -> source), restartSettings)

let startProjection<'TAggregate, 'TEvent>
   (conf: ReadModelSyncConfig<'TAggregate, 'TEvent>)
   (mailbox: Eventsourced<obj>)
   (state: State)
   =
   logInfo
      mailbox
      $"Start {conf.EventJournalTag} projection at offset {state.Offset.Value}."

   let system = mailbox.System

   let readJournalSource =
      initReadJournalSource
         mailbox
         state
         conf.Chunking
         conf.RestartSettings
         conf.EventJournalTag

   let failedWritesSource, failedWritesRef = initFailedWritesSource system

   let chunking = {
      conf.Chunking with
         Duration = TimeSpan.FromSeconds 6
   }

   let getAggregate ((aggId, evts): Guid * 'TEvent list) = task {
      let! res = conf.GetAggregate aggId
      return res |> Option.map (fun agg -> agg, evts)
   }

   let bulkWriteFlow, _ =
      initBulkWriteFlow<'TAggregate * 'TEvent list>
         system
         conf.RestartSettings
         chunking
         {
            RetryAfter = conf.RetryPersistenceAfter
            persist =
               fun (props: ('TAggregate * 'TEvent list) seq) ->
                  props
                  |> Seq.fold
                        (fun (aggAcc, eventsAcc) grouping ->
                           let agg, aggEvents = grouping

                           agg :: aggAcc, List.append eventsAcc aggEvents)
                        ([], [])
                  |> conf.UpsertReadModels
            // Feed failed upserts back into the stream
            onRetry =
               fun (props: ('TAggregate * 'TEvent list) seq) ->
                  for _, events in props do
                     failedWritesRef <! events
            onPersistOk =
               fun _ response ->
                  logInfo
                     mailbox
                     $"Saved aggregate read models for journal {conf.EventJournalTag} {response}"
         }


   let flow =
      Flow.id<'TEvent list, 'TAggregate * 'TEvent list>
      |> Flow.asyncMap 1000 (fun events -> async {
         let distinctAggregateIds =
            events
            |> List.fold
                  (fun acc evt ->
                     let aggId = conf.GetAggregateIdFromEvent evt

                     match Map.tryFind aggId acc with
                     | None -> Map.add aggId [ evt ] acc
                     | Some found ->
                        Map.change
                           aggId
                           (Option.map (fun evts -> evt :: evts))
                           acc)
                  Map.empty<Guid, 'TEvent list>
            |> Map.toArray
            |> Array.map getAggregate

         let! res = Task.WhenAll(distinctAggregateIds) |> Async.AwaitTask
         return res |> Array.toSeq
      })
      |> Flow.collect id
      |> Flow.choose id
      |> Flow.via bulkWriteFlow

   readJournalSource
   |> Source.merge failedWritesSource
   |> Source.via flow
   |> Source.runWith (system.Materializer()) Sink.ignore


let actorProps<'TAggregate, 'TEvent>
   (conf: ReadModelSyncConfig<'TAggregate, 'TEvent>)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logInfo = logInfo mailbox

      let rec loop (state: State) = actor {
         let! msg = mailbox.Receive()

         match box msg with
         | :? SnapshotOffer as o -> return! loop <| unbox o.Snapshot
         | :? Message as msg ->
            match msg with
            | SaveOffset offset ->
               logInfo
                  $"Saving offset for journal {conf.EventJournalTag}: {offset.Value}"

               let newState = { Offset = offset }

               match offset.Value % 10L = 0 with
               | true -> return! loop newState <@> SaveSnapshot newState
               | false -> return! loop newState
         | msg ->
            return
               PersistentActorEventHandler.handleEvent
                  {
                     PersistentActorEventHandler.init with
                        RecoveryCompleted =
                           fun mailbox ->
                              startProjection conf mailbox state |> ignored
                  }
                  mailbox
                  msg
      }

      loop { Offset = Query.Sequence(0L) }

   propsPersist handler
