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

open Lib.SharedTypes
open Lib.Types
open Lib.Postgres
open ActorUtil
open Bank.Account.Domain
open Lib.BulkWriteStreamFlow
open AccountSqlMapper

type BulkAccountUpsert = AccountState list -> Result<List<int>, Err> Task

type AccountEventConsumerState = {
   Offset: Akka.Persistence.Query.Sequence
}

type AccountEventConsumerMessage = SaveOffset of Akka.Persistence.Query.Sequence

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
   (restartSettings: Akka.Streams.RestartSettings)
   : Source<Guid, Akka.NotUsed>
   =
   let source =
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

   RestartSource.OnFailuresWithBackoff((fun _ -> source), restartSettings)


let startProjection
   (mailbox: Eventsourced<obj>)
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryPersistenceAfter: TimeSpan)
   (upsertAccounts: BulkAccountUpsert)
   (state: AccountEventConsumerState)
   =
   logInfo
      mailbox
      $"Start AccountEvent projection at offset {state.Offset.Value}."

   let system = mailbox.System

   let readJournalSource =
      initReadJournalSource mailbox state chunking restartSettings

   let failedWritesSource, failedWritesRef = initFailedWritesSource system

   let chunking = {
      chunking with
         Duration = TimeSpan.FromSeconds 6
   }

   let bulkWriteFlow, _ =
      initBulkWriteFlow<AccountState> system restartSettings chunking {
         RetryAfter = retryPersistenceAfter
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
   (retryPersistenceAfter: TimeSpan)
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
                                 retryPersistenceAfter
                                 upsertAccounts
                                 state
                              |> ignored
                  }
                  mailbox
                  msg
      }

      loop { Offset = Query.Sequence(0L) }

   propsPersist handler

let upsertAccounts (accounts: AccountState list) =
   let sqlParams =
      accounts
      |> List.map (fun account -> [
         "@id", AccountSqlWriter.entityId account.EntityId
         "@email", AccountSqlWriter.email account.Email
         "@firstName", AccountSqlWriter.firstName account.FirstName
         "@lastName", AccountSqlWriter.lastName account.LastName
         "@balance", AccountSqlWriter.balance account.Balance
         "@currency", AccountSqlWriter.currency account.Currency
         "@status", AccountSqlWriter.status account.Status
         "@dailyDebitLimit",
         AccountSqlWriter.dailyDebitLimit account.DailyDebitLimit
         "@dailyDebitAccrued",
         AccountSqlWriter.dailyDebitAccrued account.DailyDebitAccrued
         "@dailyInternalTransferAccrued",
         AccountSqlWriter.dailyInternalTransferAccrued
            account.DailyInternalTransferAccrued
         "@dailyDomesticTransferAccrued",
         AccountSqlWriter.dailyDomesticTransferAccrued
            account.DailyDomesticTransferAccrued
         "@lastDebitDate", AccountSqlWriter.lastDebitDate account.LastDebitDate
         "@lastInternalTransferDate",
         AccountSqlWriter.lastInternalTransferDate
            account.LastInternalTransferDate
         "@lastDomesticTransferDate",
         AccountSqlWriter.lastDomesticTransferDate
            account.LastDomesticTransferDate
         "@lastBillingCycleDate",
         AccountSqlWriter.lastBillingCycleDate account.LastBillingCycleDate
         "@transferRecipients",
         AccountSqlWriter.transferRecipients account.TransferRecipients
         "@internalTransferSenders",
         AccountSqlWriter.internalTransferSenders
            account.InternalTransferSenders
         "@events", AccountSqlWriter.events account.Events
         "@maintenanceFeeQualifyingDepositFound",
         AccountSqlWriter.maintenanceFeeQualifyingDepositFound
            account.MaintenanceFeeCriteria.QualifyingDepositFound
         "@maintenanceFeeDailyBalanceThreshold",
         AccountSqlWriter.maintenanceFeeDailyBalanceThreshold
            account.MaintenanceFeeCriteria.DailyBalanceThreshold
         "@inProgressTransfers",
         AccountSqlWriter.inProgressTransfers account.InProgressTransfers
         "@inProgressTransfersCount",
         AccountSqlWriter.inProgressTransfersCount
            account.InProgressTransfers.Count
         "@cardLocked", AccountSqlWriter.cardLocked account.CardLocked
      ])

   pgTransaction [
      $"""
      INSERT into accounts
         ({AccountFields.entityId},
          {AccountFields.email},
          {AccountFields.firstName},
          {AccountFields.lastName},
          {AccountFields.balance},
          {AccountFields.currency},
          {AccountFields.status},
          {AccountFields.dailyDebitLimit},
          {AccountFields.dailyDebitAccrued},
          {AccountFields.dailyInternalTransferAccrued},
          {AccountFields.dailyDomesticTransferAccrued},
          {AccountFields.lastDebitDate},
          {AccountFields.lastInternalTransferDate},
          {AccountFields.lastDomesticTransferDate},
          {AccountFields.lastBillingCycleDate},
          {AccountFields.transferRecipients},
          {AccountFields.internalTransferSenders},
          {AccountFields.events},
          {AccountFields.maintenanceFeeQualifyingDepositFound},
          {AccountFields.maintenanceFeeDailyBalanceThreshold},
          {AccountFields.inProgressTransfers},
          {AccountFields.inProgressTransfersCount},
          {AccountFields.cardLocked})
      VALUES
         (@id,
          @email,
          @firstName,
          @lastName,
          @balance,
          @currency,
          @status,
          @dailyDebitLimit,
          @dailyDebitAccrued,
          @dailyInternalTransferAccrued,
          @dailyDomesticTransferAccrued,
          @lastDebitDate,
          @lastInternalTransferDate,
          @lastDomesticTransferDate,
          @lastBillingCycleDate,
          @transferRecipients,
          @internalTransferSenders,
          @events,
          @maintenanceFeeQualifyingDepositFound,
          @maintenanceFeeDailyBalanceThreshold,
          @inProgressTransfers,
          @inProgressTransfersCount,
          @cardLocked)
      ON CONFLICT ({AccountFields.entityId})
      DO UPDATE SET
         {AccountFields.balance} = @balance,
         {AccountFields.status} = @status,
         {AccountFields.dailyDebitLimit} = @dailyDebitLimit,
         {AccountFields.dailyDebitAccrued} = @dailyDebitAccrued,
         {AccountFields.dailyInternalTransferAccrued} = @dailyInternalTransferAccrued,
         {AccountFields.dailyDomesticTransferAccrued} = @dailyDomesticTransferAccrued,
         {AccountFields.lastDebitDate} = @lastDebitDate,
         {AccountFields.lastInternalTransferDate} = @lastInternalTransferDate,
         {AccountFields.lastDomesticTransferDate} = @lastDomesticTransferDate,
         {AccountFields.lastBillingCycleDate} = @lastBillingCycleDate,
         {AccountFields.transferRecipients} = @transferRecipients,
         {AccountFields.internalTransferSenders} = @internalTransferSenders,
         {AccountFields.events} = @events,
         {AccountFields.maintenanceFeeQualifyingDepositFound} = @maintenanceFeeQualifyingDepositFound,
         {AccountFields.maintenanceFeeDailyBalanceThreshold} = @maintenanceFeeDailyBalanceThreshold,
         {AccountFields.inProgressTransfers} = @inProgressTransfers,
         {AccountFields.inProgressTransfersCount} = @inProgressTransfersCount,
         {AccountFields.cardLocked} = @cardLocked;
      """,
      sqlParams
   ]

let initProps
   (getAccountRef: EntityRefGetter<AccountMessage>)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryPersistenceAfter: TimeSpan)
   =
   actorProps
      getAccountRef
      chunking
      restartSettings
      retryPersistenceAfter
      upsertAccounts

let get (system: ActorSystem) : IActorRef<AccountClosureMessage> =
   typed <| ActorRegistry.For(system).Get<ActorMetadata.AccountClosureMarker>()
