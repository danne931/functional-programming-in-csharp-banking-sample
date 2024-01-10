[<RequireQualifiedAccess>]
module AccountEventConsumerActor

open System
open System.Threading.Tasks
open Akka.Hosting
open Akka.Actor
open Akka.Persistence
open Akka.Streams
open Akka.Streams.Dsl
open Akkling
open Akkling.Persistence
open Akkling.Streams
open FsToolkit.ErrorHandling

open Lib.Types
open ActorUtil
open Bank.Account.Domain
open Lib.BulkWriteStreamFlow

type BulkAccountUpsert = AccountState list -> Result<List<int>, Err> Task

let initFailedWritesSource
   (system: ActorSystem)
   : Source<Guid, Akka.NotUsed> * IActorRef<Guid>
   =
   let failedWritesSource = Source.actorRef OverflowStrategy.DropHead 1000
   let preMat = failedWritesSource.PreMaterialize system

   preMat.Last(), preMat.Head()

let initReadJournalSource
   (mailbox: Eventsourced<obj>)
   (state: AccountEventConsumerState)
   (chunking: StreamChunking)
   : Source<Guid, Akka.NotUsed>
   =
   (readJournal mailbox.System).EventsByTag("AccountEvent", state.Offset)
   |> Source.groupedWithin chunking.Size chunking.Duration
   |> Source.collect (fun evtSeq ->
      let setOfAccountIds =
         evtSeq
         |> Seq.map (fun env -> Guid.Parse env.PersistenceId)
         |> Set.ofSeq

      let offset = (Seq.last evtSeq).Offset

      match offset with
      | :? Query.Sequence as offset -> mailbox.Self <! SaveOffset offset
      | _ -> ()

      setOfAccountIds)

let startProjection
   (mailbox: Eventsourced<obj>)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (upsertAccounts: BulkAccountUpsert)
   (state: AccountEventConsumerState)
   =
   logInfo
      mailbox
      $"Start AccountEvent projection at offset {state.Offset.Value}."

   let system = mailbox.System

   let readJournalSource = initReadJournalSource mailbox state chunking

   let failedWritesSource, failedWritesRef = initFailedWritesSource system

   let chunking = {
      chunking with
         Duration = TimeSpan.FromSeconds 6
   }

   let bulkWriteFlow, _ =
      initBulkWriteFlow<AccountState> system restartSettings chunking {
         persist =
            fun (accounts: AccountState seq) ->
               accounts |> List.ofSeq |> upsertAccounts
         // Feed failed upserts back into the stream
         onRetry =
            fun (accounts: AccountState seq) ->
               for account in accounts do
                  failedWritesRef <! account.EntityId
         onPersistOk =
            fun _ response ->
               SystemLog.info system $"Saved account read models {response}"
      }

   let flow =
      Flow.id
      |> Flow.asyncMapUnordered 10 (fun accountId -> async {
         let! (accountOpt: AccountState option) =
            getAccountRef accountId <? GetAccount

         return accountOpt
      })
      |> Flow.choose id
      |> Flow.via bulkWriteFlow

   readJournalSource
   |> Source.merge failedWritesSource
   |> Source.via flow
   |> Source.runWith (system.Materializer()) Sink.ignore

let actorProps
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (upsertAccounts: BulkAccountUpsert)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logInfo = logInfo mailbox

      let rec loop (state: AccountEventConsumerState) = actor {
         let! msg = mailbox.Receive()

         match box msg with
         | :? SnapshotOffer as o -> return! loop <| unbox o.Snapshot
         | :? AccountEventConsumerMessage as msg ->
            match msg with
            | SaveOffset offset ->
               logInfo $"Saving offset: {offset.Value}"
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
                              startProjection
                                 mailbox
                                 getAccountRef
                                 chunking
                                 restartSettings
                                 upsertAccounts
                                 state
                              |> ignored
                  }
                  mailbox
                  msg
      }

      loop { Offset = Query.Sequence(0L) }

   propsPersist handler

let get (system: ActorSystem) : IActorRef<AccountClosureMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.AccountClosureMarker>()
