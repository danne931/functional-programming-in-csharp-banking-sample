[<RequireQualifiedAccess>]
module EmployeeReadModelSyncActor

open System
open Akkling
open Akkling.Cluster.Sharding
open FsToolkit.ErrorHandling

open Lib.SharedTypes
open Lib.Types
open Lib.Postgres
open Bank.Employee.Domain
open Lib.ReadModelSyncActor
open EmployeeSqlMapper
open EmployeeEventSqlMapper
open CardSqlMapper

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
          | EmployeeEvent.MonthlyDebitLimitUpdated e ->
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
   actorProps<Employee, EmployeeEvent> (
      {
         GetAggregateIdFromEvent =
            EmployeeEnvelope.unwrap >> snd >> _.EntityId >> EntityId.get
         GetAggregate =
            fun employeeId -> task {
               let aref = getEmployeeRef (EmployeeId employeeId)
               let! (opt: Employee option) = aref <? EmployeeMessage.GetEmployee
               return opt
            }
         Chunking = chunking
         RestartSettings = restartSettings
         RetryPersistenceAfter = retryPersistenceAfter
         UpsertReadModels = upsertReadModels
         EventJournalTag = Constants.AKKA_EMPLOYEE_JOURNAL
      }
   )
