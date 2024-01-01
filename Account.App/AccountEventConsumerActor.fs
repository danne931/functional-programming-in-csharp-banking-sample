[<RequireQualifiedAccess>]
module AccountEventConsumerActor

open System
open System.Threading.Tasks
open Akka.Hosting
open Akka.Actor
open Akka.Persistence
open Akkling
open Akkling.Persistence
open Akkling.Streams
open FsToolkit.ErrorHandling

open Lib.Types
open ActorUtil
open Bank.Account.Domain

type BulkAccountUpsert = AccountState list -> Result<List<int>, Err> Task

let startProjection
   (mailbox: Eventsourced<obj>)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (chunking: StreamChunking)
   (upsertAccounts: BulkAccountUpsert)
   (state: AccountEventConsumerState)
   =
   logInfo
      mailbox
      $"Start AccountEvent projection at offset {state.Offset.Value}."

   let source =
      (readJournal mailbox.System).EventsByTag("AccountEvent", state.Offset)

   source
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
   |> Source.asyncMapUnordered 10 (fun accountId -> async {
      let! (accountOpt: AccountState option) =
         getAccountRef accountId <? GetAccount

      return accountOpt
   })
   |> Source.choose id
   |> Source.groupedWithin chunking.Size (TimeSpan.FromSeconds 6)
   |> Source.taskMap 1 (List.ofSeq >> upsertAccounts)
   |> Source.map (fun result ->
      printfn "output: %A" result
      result)
   |> Source.runWith (mailbox.System.Materializer()) Sink.ignore

let actorProps
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (chunking: StreamChunking)
   (upsertAccounts: BulkAccountUpsert)
   =
   let handler (mailbox: Eventsourced<obj>) =
      let logInfo, logError = logInfo mailbox, logError mailbox

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
