[<RequireQualifiedAccess>]
module ReadModelSyncActor

open System
open System.Threading.Tasks
open Akka.Actor
open Akka.Persistence
open Akka.Streams
open Akka.Streams.Dsl
open Akkling
open Akkling.Persistence
open Akkling.Streams
open Akkling.Cluster.Sharding
open FsToolkit.ErrorHandling

open Lib.SharedTypes
open Lib.Types
open Lib.Postgres
open ActorUtil
open Bank.Account.Domain
open Lib.BulkWriteStreamFlow
open AccountSqlMapper
open TransactionSqlMapper

type EventsGroupedByAccount = Account * AccountEvent list

type ReadModelUpsert =
   Account list * AccountEvent list -> Result<List<int>, Err> Task

type State = {
   Offset: Akka.Persistence.Query.Sequence
}

type Message = SaveOffset of Akka.Persistence.Query.Sequence

let initFailedWritesSource
   (system: ActorSystem)
   : Source<AccountEvent list, Akka.NotUsed> * IActorRef<AccountEvent list>
   =
   let failedWritesSource = Source.actorRef OverflowStrategy.DropHead 1000
   let preMat = failedWritesSource.PreMaterialize system

   preMat.Last(), preMat.Head()

let initReadJournalSource
   (mailbox: Eventsourced<obj>)
   (state: State)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   : Source<AccountEvent list, Akka.NotUsed>
   =
   let source =
      (readJournal mailbox.System).EventsByTag("AccountEvent", state.Offset)
      |> Source.groupedWithin chunking.Size chunking.Duration
      |> Source.map (fun evtSeq ->
         let evts: AccountEvent list =
            evtSeq |> Seq.map (fun env -> unbox env.Event) |> List.ofSeq

         let offset = (Seq.last evtSeq).Offset

         match offset with
         | :? Query.Sequence as offset -> mailbox.Self <! SaveOffset offset
         | _ -> ()

         evts)

   RestartSource.OnFailuresWithBackoff((fun _ -> source), restartSettings)

let startProjection
   (mailbox: Eventsourced<obj>)
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryPersistenceAfter: TimeSpan)
   (upsertReadModels: ReadModelUpsert)
   (state: State)
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
      initBulkWriteFlow<EventsGroupedByAccount> system restartSettings chunking {
         RetryAfter = retryPersistenceAfter
         persist =
            fun (props: EventsGroupedByAccount seq) ->
               let props =
                  props
                  |> Seq.fold
                        (fun (accountAcc, eventsAcc) grouping ->
                           let account, accountEvents = grouping

                           account :: accountAcc,
                           List.append eventsAcc accountEvents)
                        ([], [])

               upsertReadModels props
         // Feed failed upserts back into the stream
         onRetry =
            fun (props: EventsGroupedByAccount seq) ->
               for _, accountEvents in props do
                  failedWritesRef <! accountEvents
         onPersistOk =
            fun _ response ->
               logInfo mailbox $"Saved account read models {response}"
      }

   let getAccount (accountId: AccountId, accountEvents: AccountEvent list) = task {
      let! (accountOpt: Account option) = getAccountRef accountId <? GetAccount

      return accountOpt |> Option.map (fun account -> account, accountEvents)
   }

   let flow =
      Flow.id<AccountEvent list, EventsGroupedByAccount>
      |> Flow.asyncMap 1000 (fun accountEvents -> async {
         let distinctAccountIds =
            accountEvents
            |> List.fold
                  (fun acc accountEvent ->
                     let _, envelope = AccountEnvelope.unwrap accountEvent
                     let accountId = AccountId.fromEntityId envelope.EntityId

                     match Map.tryFind accountId acc with
                     | None -> Map.add accountId [ accountEvent ] acc
                     | Some found ->
                        Map.change
                           accountId
                           (Option.map (fun accountEvents ->
                              accountEvent :: accountEvents))
                           acc)
                  Map.empty<AccountId, AccountEvent list>
            |> Map.toArray
            |> Array.map getAccount

         let! res = Task.WhenAll(distinctAccountIds) |> Async.AwaitTask
         return res |> Array.toSeq
      })
      |> Flow.collect id
      |> Flow.choose id
      |> Flow.via bulkWriteFlow

   readJournalSource
   |> Source.merge failedWritesSource
   |> Source.via flow
   |> Source.runWith (system.Materializer()) Sink.ignore

let actorProps
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryPersistenceAfter: TimeSpan)
   (upsertReadModels: ReadModelUpsert)
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
                                 upsertReadModels
                                 state
                              |> ignored
                  }
                  mailbox
                  msg
      }

      loop { Offset = Query.Sequence(0L) }

   propsPersist handler

let upsertReadModels
   (accounts: Account list, accountEvents: AccountEvent list)
   =
   let accountSqlParams =
      accounts
      |> List.map (fun account -> [
         "id", AccountSqlWriter.accountId account.AccountId
         "accountNumber", AccountSqlWriter.accountNumber account.AccountNumber
         "routingNumber", AccountSqlWriter.routingNumber account.RoutingNumber
         "orgId", AccountSqlWriter.orgId account.OrgId
         "name", AccountSqlWriter.name account.Name
         "depository", AccountSqlWriter.depository account.Depository
         "balance", AccountSqlWriter.balance account.Balance
         "currency", AccountSqlWriter.currency account.Currency
         "status", AccountSqlWriter.status account.Status
         "lastBillingCycleDate",
         AccountSqlWriter.lastBillingCycleDate account.LastBillingCycleDate

         "internalTransferRecipients",
         AccountSqlWriter.internalTransferRecipients
            account.InternalTransferRecipients

         "domesticTransferRecipients",
         AccountSqlWriter.domesticTransferRecipients
            account.DomesticTransferRecipients

         "internalTransferSenders",
         AccountSqlWriter.internalTransferSenders
            account.InternalTransferSenders

         "events", AccountSqlWriter.events account.Events

         "maintenanceFeeQualifyingDepositFound",
         AccountSqlWriter.maintenanceFeeQualifyingDepositFound
            account.MaintenanceFeeCriteria.QualifyingDepositFound

         "maintenanceFeeDailyBalanceThreshold",
         AccountSqlWriter.maintenanceFeeDailyBalanceThreshold
            account.MaintenanceFeeCriteria.DailyBalanceThreshold

         "inProgressInternalTransfers",
         AccountSqlWriter.inProgressInternalTransfers
            account.InProgressInternalTransfers

         "inProgressInternalTransfersCount",
         AccountSqlWriter.transfersCount
            account.InProgressInternalTransfers.Count

         "inProgressDomesticTransfers",
         AccountSqlWriter.domesticTransfers account.InProgressDomesticTransfers

         "inProgressDomesticTransfersCount",
         AccountSqlWriter.transfersCount
            account.InProgressDomesticTransfers.Count

         "failedDomesticTransfers",
         AccountSqlWriter.domesticTransfers account.FailedDomesticTransfers

         "failedDomesticTransfersCount",
         AccountSqlWriter.transfersCount account.FailedDomesticTransfers.Count
      ])

   let transactionSqlParams =
      accountEvents
      |> List.map (fun evt ->
         let evt, envelope = AccountEnvelope.unwrap evt

         let sqlParams = [
            "transactionId", TransactionSqlWriter.transactionId envelope.Id

            "accountId",
            envelope.EntityId
            |> AccountId.fromEntityId
            |> TransactionSqlWriter.accountId

            "orgId", TransactionSqlWriter.orgId envelope.OrgId

            "correlationId",
            TransactionSqlWriter.correlationId envelope.CorrelationId

            "name", TransactionSqlWriter.name envelope.EventName
            "timestamp", TransactionSqlWriter.timestamp envelope.Timestamp
            "event", TransactionSqlWriter.event evt
         ]

         let amountOpt, moneyFlowOpt =
            match evt with
            | AccountEvent.CreatedAccount evt -> Some evt.Data.Balance, None
            | AccountEvent.DepositedCash evt ->
               Some evt.Data.Amount, Some MoneyFlow.In
            | AccountEvent.DebitedAccount evt ->
               Some evt.Data.Amount, Some MoneyFlow.Out
            | AccountEvent.InternalTransferPending evt ->
               Some evt.Data.BaseInfo.Amount, Some MoneyFlow.Out
            | AccountEvent.InternalTransferRejected evt ->
               Some evt.Data.BaseInfo.Amount, Some MoneyFlow.In
            | AccountEvent.TransferDeposited evt ->
               Some evt.Data.Amount, Some MoneyFlow.In
            | AccountEvent.DomesticTransferPending evt ->
               Some evt.Data.BaseInfo.Amount, Some MoneyFlow.Out
            | AccountEvent.DomesticTransferRejected evt ->
               Some evt.Data.BaseInfo.Amount, Some MoneyFlow.In
            | AccountEvent.MaintenanceFeeDebited evt ->
               Some evt.Data.Amount, Some MoneyFlow.Out
            | _ -> None, None

         let cardIdOpt =
            match evt with
            | AccountEvent.DebitedAccount e ->
               Some e.Data.EmployeePurchaseReference.CardId
            | _ -> None

         sqlParams
         @ [
            ("amount", TransactionSqlWriter.amount amountOpt)
            ("moneyFlow", TransactionSqlWriter.moneyFlow moneyFlowOpt)
            ("cardId", TransactionSqlWriter.cardId cardIdOpt)
         ])

   pgTransaction [
      $"""
      INSERT into {AccountSqlMapper.table}
         ({AccountFields.accountId},
          {AccountFields.accountNumber},
          {AccountFields.routingNumber},
          {AccountFields.orgId},
          {AccountFields.name},
          {AccountFields.depository},
          {AccountFields.balance},
          {AccountFields.currency},
          {AccountFields.status},
          {AccountFields.lastBillingCycleDate},
          {AccountFields.internalTransferRecipients},
          {AccountFields.domesticTransferRecipients},
          {AccountFields.internalTransferSenders},
          {AccountFields.events},
          {AccountFields.maintenanceFeeQualifyingDepositFound},
          {AccountFields.maintenanceFeeDailyBalanceThreshold},
          {AccountFields.inProgressInternalTransfers},
          {AccountFields.inProgressInternalTransfersCount},
          {AccountFields.inProgressDomesticTransfers},
          {AccountFields.inProgressDomesticTransfersCount},
          {AccountFields.failedDomesticTransfers},
          {AccountFields.failedDomesticTransfersCount})
      VALUES
         (@id,
          @accountNumber,
          @routingNumber,
          @orgId,
          @name,
          @depository::{AccountTypeCast.depository},
          @balance,
          @currency,
          @status::{AccountTypeCast.status},
          @lastBillingCycleDate,
          @internalTransferRecipients,
          @domesticTransferRecipients,
          @internalTransferSenders,
          @events,
          @maintenanceFeeQualifyingDepositFound,
          @maintenanceFeeDailyBalanceThreshold,
          @inProgressInternalTransfers,
          @inProgressInternalTransfersCount,
          @inProgressDomesticTransfers,
          @inProgressDomesticTransfersCount,
          @failedDomesticTransfers,
          @failedDomesticTransfersCount)
      ON CONFLICT ({AccountFields.accountId})
      DO UPDATE SET
         {AccountFields.balance} = @balance,
         {AccountFields.status} = @status::{AccountTypeCast.status},
         {AccountFields.lastBillingCycleDate} = @lastBillingCycleDate,
         {AccountFields.internalTransferRecipients} = @internalTransferRecipients,
         {AccountFields.domesticTransferRecipients} = @domesticTransferRecipients,
         {AccountFields.internalTransferSenders} = @internalTransferSenders,
         {AccountFields.events} = @events,
         {AccountFields.maintenanceFeeQualifyingDepositFound} = @maintenanceFeeQualifyingDepositFound,
         {AccountFields.maintenanceFeeDailyBalanceThreshold} = @maintenanceFeeDailyBalanceThreshold,
         {AccountFields.inProgressInternalTransfers} = @inProgressInternalTransfers,
         {AccountFields.inProgressInternalTransfersCount} = @inProgressInternalTransfersCount,
         {AccountFields.inProgressDomesticTransfers} = @inProgressDomesticTransfers,
         {AccountFields.inProgressDomesticTransfersCount} = @inProgressDomesticTransfersCount,
         {AccountFields.failedDomesticTransfers} = @failedDomesticTransfers,
         {AccountFields.failedDomesticTransfersCount} = @failedDomesticTransfersCount;
      """,
      accountSqlParams

      $"""
      INSERT into {TransactionSqlMapper.table}
         ({TransactionFields.transactionId},
          {TransactionFields.accountId},
          {TransactionFields.orgId},
          {TransactionFields.correlationId},
          {TransactionFields.cardId},
          {TransactionFields.name},
          {TransactionFields.timestamp},
          {TransactionFields.event},
          {TransactionFields.amount},
          {TransactionFields.moneyFlow})
      VALUES
         (@transactionId,
          @accountId,
          @orgId,
          @correlationId,
          @cardId,
          @name,
          @timestamp,
          @event,
          @amount,
          @moneyFlow::{TransactionTypeCast.moneyFlow})
      ON CONFLICT ({TransactionFields.transactionId})
      DO NOTHING;
      """,
      transactionSqlParams
   ]

let initProps
   (getAccountRef: AccountId -> IEntityRef<AccountMessage>)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryPersistenceAfter: TimeSpan)
   =
   actorProps
      getAccountRef
      chunking
      restartSettings
      retryPersistenceAfter
      upsertReadModels
