[<RequireQualifiedAccess>]
module EmployeeReadModelSyncActor

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
open Bank.Employee.Domain
open Lib.BulkWriteStreamFlow
open EmployeeSqlMapper
open EmployeeEventSqlMapper
open CardSqlMapper

type EventsGroupedByEmployee = Employee * EmployeeEvent list

type ReadModelUpsert =
   Employee list * EmployeeEvent list -> Result<List<int>, Err> Task

type State = {
   Offset: Akka.Persistence.Query.Sequence
}

type Message = SaveOffset of Akka.Persistence.Query.Sequence

let initFailedWritesSource
   (system: ActorSystem)
   : Source<EmployeeEvent list, Akka.NotUsed> * IActorRef<EmployeeEvent list>
   =
   let failedWritesSource = Source.actorRef OverflowStrategy.DropHead 1000
   let preMat = failedWritesSource.PreMaterialize system

   preMat.Last(), preMat.Head()

let initReadJournalSource
   (mailbox: Eventsourced<obj>)
   (state: State)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   : Source<EmployeeEvent list, Akka.NotUsed>
   =
   let source =
      (readJournal mailbox.System).EventsByTag("EmployeeEvent", state.Offset)
      |> Source.groupedWithin chunking.Size chunking.Duration
      |> Source.map (fun evtSeq ->
         let evts: EmployeeEvent list =
            evtSeq |> Seq.map (fun env -> unbox env.Event) |> List.ofSeq

         let offset = (Seq.last evtSeq).Offset

         match offset with
         | :? Query.Sequence as offset -> mailbox.Self <! SaveOffset offset
         | _ -> ()

         evts)

   RestartSource.OnFailuresWithBackoff((fun _ -> source), restartSettings)

// TODO: Much of this actor logic is identical to the AccountReadModelSync
//       actor except for the upsertReadModels implementation.
//       See if possible to abstract so any "ReadModelSync" actor
//       can just define it's upsertReadModels implementation.
let startProjection
   (mailbox: Eventsourced<obj>)
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryPersistenceAfter: TimeSpan)
   (upsertReadModels: ReadModelUpsert)
   (state: State)
   =
   logInfo
      mailbox
      $"Start EmployeeEvent projection at offset {state.Offset.Value}."

   let system = mailbox.System

   let readJournalSource =
      initReadJournalSource mailbox state chunking restartSettings

   let failedWritesSource, failedWritesRef = initFailedWritesSource system

   let chunking = {
      chunking with
         Duration = TimeSpan.FromSeconds 6
   }

   let bulkWriteFlow, _ =
      initBulkWriteFlow<EventsGroupedByEmployee> system restartSettings chunking {
         RetryAfter = retryPersistenceAfter
         persist =
            fun (props: EventsGroupedByEmployee seq) ->
               let props =
                  props
                  |> Seq.fold
                        (fun (employeeAcc, eventsAcc) grouping ->
                           let employee, employeeEvents = grouping

                           employee :: employeeAcc,
                           List.append eventsAcc employeeEvents)
                        ([], [])

               upsertReadModels props
         // Feed failed upserts back into the stream
         onRetry =
            fun (props: EventsGroupedByEmployee seq) ->
               for _, employeeEvents in props do
                  failedWritesRef <! employeeEvents
         onPersistOk =
            fun _ response ->
               logInfo mailbox $"Saved employee read models {response}"
      }

   let getEmployee
      (employeeId: EmployeeId, employeeEvents: EmployeeEvent list)
      =
      task {
         let! (employeeOpt: Employee option) =
            getEmployeeRef employeeId <? GetEmployee

         return
            employeeOpt |> Option.map (fun employee -> employee, employeeEvents)
      }

   let flow =
      Flow.id<EmployeeEvent list, EventsGroupedByEmployee>
      |> Flow.asyncMap 1000 (fun employeeEvents -> async {
         let distinctEmployeeIds =
            employeeEvents
            |> List.fold
                  (fun acc employeeEvent ->
                     let _, envelope = EmployeeEnvelope.unwrap employeeEvent

                     let employeeId =
                        EmployeeId.fromEntityId envelope.EntityId

                     match Map.tryFind employeeId acc with
                     | None -> Map.add employeeId [ employeeEvent ] acc
                     | Some found ->
                        Map.change
                           employeeId
                           (Option.map (fun employeeEvents ->
                              employeeEvent :: employeeEvents))
                           acc)
                  Map.empty<EmployeeId, EmployeeEvent list>
            |> Map.toArray
            |> Array.map getEmployee

         let! res = Task.WhenAll(distinctEmployeeIds) |> Async.AwaitTask
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
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
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
                                 getEmployeeRef
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
   (employees: Employee list, employeeEvents: EmployeeEvent list)
   =
   let employeeSqlParams =
      employees
      |> List.map (fun employee -> [
         "employeeId", EmployeeSqlWriter.employeeId employee.EmployeeId
         "orgId", EmployeeSqlWriter.orgId employee.OrgId
         "email", EmployeeSqlWriter.email employee.Email
         "firstName", EmployeeSqlWriter.firstName employee.FirstName
         "lastName", EmployeeSqlWriter.lastName employee.LastName
         "status", EmployeeSqlWriter.status employee.Status
         "role", EmployeeSqlWriter.role employee.Role
         "cards", EmployeeSqlWriter.cards employee.Cards
         "pendingPurchases",
         EmployeeSqlWriter.pendingPurchases employee.PendingPurchases
         "onboardingTasks",
         EmployeeSqlWriter.onboardingTasks employee.OnboardingTasks
         "inviteToken", EmployeeSqlWriter.inviteTokenFromStatus employee.Status
         "inviteExpiration",
         EmployeeSqlWriter.inviteExpirationFromStatus employee.Status
         "authProviderUserId",
         EmployeeSqlWriter.authProviderUserId employee.AuthProviderUserId
      ])

   let eventSqlParams =
      employeeEvents
      |> List.map (fun evt ->
         let evt, envelope = EmployeeEnvelope.unwrap evt

         [
            "eventId", EmployeeEventSqlWriter.eventId envelope.Id

            "employeeId",
            envelope.EntityId
            |> EmployeeId.fromEntityId
            |> EmployeeEventSqlWriter.employeeId

            "initiatedById",
            envelope.InitiatedById |> EmployeeEventSqlWriter.initiatedById

            "orgId", EmployeeEventSqlWriter.orgId envelope.OrgId

            "correlationId",
            EmployeeEventSqlWriter.correlationId envelope.CorrelationId

            "name", EmployeeEventSqlWriter.name envelope.EventName
            "timestamp", EmployeeEventSqlWriter.timestamp envelope.Timestamp
            "event", EmployeeEventSqlWriter.event evt
         ])

   let query = [
      $"""
      INSERT into {EmployeeSqlMapper.table}
         ({EmployeeFields.employeeId},
          {EmployeeFields.orgId},
          {EmployeeFields.role},
          {EmployeeFields.email},
          {EmployeeFields.firstName},
          {EmployeeFields.lastName},
          {EmployeeFields.cards},
          {EmployeeFields.status},
          {EmployeeFields.pendingPurchases},
          {EmployeeFields.onboardingTasks},
          {EmployeeFields.inviteToken},
          {EmployeeFields.inviteExpiration},
          {EmployeeFields.authProviderUserId})
      VALUES
         (@employeeId,
          @orgId,
          @role::{EmployeeTypeCast.role},
          @email,
          @firstName,
          @lastName,
          @cards,
          @status::{EmployeeTypeCast.status},
          @pendingPurchases,
          @onboardingTasks,
          @inviteToken,
          @inviteExpiration,
          @authProviderUserId)
      ON CONFLICT ({EmployeeFields.employeeId})
      DO UPDATE SET
         {EmployeeFields.status} = @status::{EmployeeTypeCast.status},
         {EmployeeFields.cards} = @cards,
         {EmployeeFields.pendingPurchases} = @pendingPurchases,
         {EmployeeFields.onboardingTasks} = @onboardingTasks,
         {EmployeeFields.role} = @role::{EmployeeTypeCast.role},
         {EmployeeFields.inviteToken} = @inviteToken,
         {EmployeeFields.inviteExpiration} = @inviteExpiration,
         {EmployeeFields.authProviderUserId} = @authProviderUserId;
      """,
      employeeSqlParams

      $"""
      INSERT into {EmployeeEventSqlMapper.table}
         ({EmployeeEventFields.eventId},
          {EmployeeEventFields.orgId},
          {EmployeeEventFields.employeeId},
          {EmployeeEventFields.initiatedById},
          {EmployeeEventFields.correlationId},
          {EmployeeEventFields.name},
          {EmployeeEventFields.timestamp},
          {EmployeeEventFields.event})
      VALUES
         (@eventId,
          @orgId,
          @employeeId,
          @initiatedById,
          @correlationId,
          @name,
          @timestamp,
          @event)
      ON CONFLICT ({EmployeeEventFields.eventId})
      DO NOTHING;
      """,
      eventSqlParams
   ]

   let cardSqlParams =
      let employeeMap = [ for e in employees -> e.EmployeeId, e ] |> Map.ofList

      employeeEvents
      |> List.choose (fun evt ->
         (match evt with
          | EmployeeEvent.CreatedCard e -> Some(e.EntityId, e.Data.Card.CardId)
          | EmployeeEvent.DebitApproved e ->
             Some(e.EntityId, e.Data.Info.CardId)
          | EmployeeEvent.LockedCard e -> Some(e.EntityId, e.Data.CardId)
          | EmployeeEvent.UnlockedCard e -> Some(e.EntityId, e.Data.CardId)
          | EmployeeEvent.DailyDebitLimitUpdated e ->
             Some(e.EntityId, e.Data.CardId)
          | EmployeeEvent.CardNicknamed e -> Some(e.EntityId, e.Data.CardId)
          | _ -> None)
         |> Option.bind (fun (entityId, cardId) ->
            employeeMap
            |> Map.tryFind (EmployeeId.fromEntityId entityId)
            |> Option.bind (fun employee ->
               employee.Cards
               |> Map.tryFind cardId
               |> Option.map (fun card -> [
                  "cardId", CardSqlWriter.cardId card.CardId
                  "accountId", CardSqlWriter.accountId card.AccountId
                  "orgId", CardSqlWriter.orgId employee.OrgId
                  "employeeId", CardSqlWriter.employeeId employee.EmployeeId

                  "cardNumberLast4",
                  CardSqlWriter.cardNumberLast4 card.CardNumberLast4

                  "cardType", CardSqlWriter.cardType card.CardType
                  "status", CardSqlWriter.status card.Status
                  "virtual", CardSqlWriter.isVirtual card.Virtual

                  "dailyPurchaseLimit",
                  CardSqlWriter.dailyPurchaseLimit card.DailyPurchaseLimit

                  "monthlyPurchaseLimit",
                  CardSqlWriter.monthlyPurchaseLimit
                     card.MonthlyPurchaseLimit

                  "lastPurchaseAt",
                  CardSqlWriter.lastPurchaseAt card.LastPurchaseAt

                  "cardNickname",
                  CardSqlWriter.cardNickname card.CardNickname

                  "expMonth", CardSqlWriter.expMonth card.Expiration.Month

                  "expYear", CardSqlWriter.expMonth card.Expiration.Year
               ]))))

   let cardQuery =
      $"""
      INSERT into {CardSqlMapper.table}
         ({CardFields.cardId},
          {CardFields.accountId},
          {CardFields.orgId},
          {CardFields.employeeId},
          {CardFields.cardNumberLast4},
          {CardFields.cardType},
          {CardFields.status},
          {CardFields.isVirtual},
          {CardFields.dailyPurchaseLimit},
          {CardFields.monthlyPurchaseLimit},
          {CardFields.lastPurchaseAt},
          {CardFields.cardNickname},
          {CardFields.expMonth},
          {CardFields.expYear})
      VALUES
         (@cardId,
          @accountId,
          @orgId,
          @employeeId,
          @cardNumberLast4,
          @cardType::{CardTypeCast.cardType},
          @status::{CardTypeCast.status},
          @virtual,
          @dailyPurchaseLimit,
          @monthlyPurchaseLimit,
          @lastPurchaseAt,
          @cardNickname,
          @expMonth,
          @expYear)
      ON CONFLICT ({CardFields.cardId})
      DO UPDATE SET
         {CardFields.status} = @status::{CardTypeCast.status},
         {CardFields.dailyPurchaseLimit} = @dailyPurchaseLimit,
         {CardFields.monthlyPurchaseLimit} = @monthlyPurchaseLimit,
         {CardFields.lastPurchaseAt} = @lastPurchaseAt,
         {CardFields.cardNickname} = @cardNickname;
      """,
      cardSqlParams

   let query = if cardSqlParams.IsEmpty then query else cardQuery :: query

   pgTransaction query

let initProps
   (getEmployeeRef: EmployeeId -> IEntityRef<EmployeeMessage>)
   (chunking: StreamChunking)
   (restartSettings: Akka.Streams.RestartSettings)
   (retryPersistenceAfter: TimeSpan)
   =
   actorProps
      getEmployeeRef
      chunking
      restartSettings
      retryPersistenceAfter
      upsertReadModels
