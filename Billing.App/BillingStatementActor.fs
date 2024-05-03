[<RequireQualifiedAccess>]
module BillingStatementActor

open System
open System.Threading.Tasks
open Akka.Actor
open Akka.Hosting
open Akkling
open Akka.Streams
open Akkling.Streams
open FsToolkit.ErrorHandling

open BillingStatement
open BillingSqlMapper
open ActorUtil
open Lib.Postgres
open Lib.SharedTypes
open Lib.Types
open Lib.BulkWriteStreamFlow

type BillingPersistence = {
   saveBillingStatements: BillingStatement list -> Task<Result<int list, Err>>
}

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

let private billingStatementSqlParams (bill: BillingStatement) = [
   "@transactions", BillingSqlWriter.transactions bill.Transactions
   "@month", BillingSqlWriter.month bill.Month
   "@year", BillingSqlWriter.year bill.Year
   "@balance", BillingSqlWriter.balance bill.Balance
   "@name", BillingSqlWriter.name bill.Name
   "@accountId", BillingSqlWriter.accountId bill.AccountId
   "@lastPersistedEventSequenceNumber",
   BillingSqlWriter.lastPersistedEventSequenceNumber
      bill.LastPersistedEventSequenceNumber
   "@accountSnapshot", BillingSqlWriter.accountSnapshot bill.AccountSnapshot
]

let private eventJournalSqlParams (bill: BillingStatement) = [
   "@persistenceId", Sql.text <| string bill.AccountId
   "@sequenceNumber", Sql.int64 bill.LastPersistedEventSequenceNumber
]

let private snapshotStoreSqlParams (bill: BillingStatement) = [
   "@persistenceId", Sql.text <| string bill.AccountId
   "@sequenceNumber", Sql.int64 bill.LastPersistedEventSequenceNumber
   "@created", Sql.int64 <| DateTime.UtcNow.ToFileTimeUtc()
   "@snapshot", Sql.bytea bill.AccountSnapshot
   "@serializerId", Sql.int 931
   "@manifest", Sql.string "Account"
]

// NOTE:
// Deleting akka_event_journal account events & taking a snapshot
// used to be implemented in the AccountActor application logic.
// These DB ops would occur for each account during a billing cycle,
// leading to Postgres connection pool errors.
// To fix this I have removed this application logic from the AccountActor,
// instead opting to carry out these DB ops in a transaction along with
// insertion of the billing statements.
let private saveBillingStatements (statements: BillingStatement list) =
   let sqlParams =
      statements
      |> List.fold
         (fun acc bill -> {|
            Billing = billingStatementSqlParams bill :: acc.Billing
            EventJournal = eventJournalSqlParams bill :: acc.EventJournal
            SnapshotStore = snapshotStoreSqlParams bill :: acc.SnapshotStore
         |})
         {|
            Billing = []
            EventJournal = []
            SnapshotStore = []
         |}

   pgTransaction [
      $"""
      INSERT into {BillingSqlMapper.table}
         ({BillingFields.transactions},
          {BillingFields.month},
          {BillingFields.year},
          {BillingFields.balance},
          {BillingFields.name},
          {BillingFields.accountId},
          {BillingFields.lastPersistedEventSequenceNumber},
          {BillingFields.accountSnapshot})
      VALUES
         (@transactions,
          @month,
          @year,
          @balance,
          @name,
          @accountId,
          @lastPersistedEventSequenceNumber,
          @accountSnapshot)
      """,
      sqlParams.Billing

      """
      UPDATE akka_event_journal
      SET
         deleted = true
      WHERE
         persistence_id = @persistenceId AND
         sequence_number <= @sequenceNumber
      """,
      sqlParams.EventJournal

      """
      INSERT into akka_snapshots
         (persistence_id,
          sequence_number,
          created,
          snapshot,
          serializer_id,
          manifest)
      VALUES
         (@persistenceId,
          @sequenceNumber,
          @created,
          @snapshot,
          @serializerId,
          @manifest)
      """,
      sqlParams.SnapshotStore
   ]

let start
   (system: ActorSystem)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryFailedPersistenceAfter: TimeSpan)
   : IActorRef<BillingStatementMessage>
   =
   spawn system ActorMetadata.billingStatement.Name
   <| actorProps
         system
         {
            saveBillingStatements = saveBillingStatements
         }
         chunking
         restartSettings
         retryFailedPersistenceAfter
   |> retype
