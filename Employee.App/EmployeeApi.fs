module Bank.Employee.Api

open Akkling
open Akka.Actor
open FsToolkit.ErrorHandling
open Validus
open System

open Lib.Postgres
open Lib.SharedTypes
open Bank.Employee.Domain
open Lib.NetworkQuery

module Fields = EmployeeSqlMapper.EmployeeFields
module Reader = EmployeeSqlMapper.EmployeeSqlReader
module Writer = EmployeeSqlMapper.EmployeeSqlWriter
let table = EmployeeSqlMapper.table

let searchEmployees (orgId: OrgId) (searchQuery: string) =
   pgQuery<Employee>
      $$"""
      SELECT * FROM {{table}}
      WHERE
         {{Fields.orgId}} = @orgId
         AND {{Fields.searchQuery}} %> @query
      ORDER BY {{Fields.searchQuery}} <-> @query DESC
      LIMIT 10
      """
      (Some [
         "orgId", Writer.orgId orgId
         "query", Writer.searchQuery searchQuery
      ])
      Reader.employee

let processCommand (system: ActorSystem) (command: EmployeeCommand) = taskResult {
   let validation =
      match command with
      | CreateEmployee cmd ->
         CreateEmployeeCommand.toEvent cmd |> Result.map EmployeeEnvelope.get
      | DebitRequest cmd ->
         DebitRequestCommand.toEvent cmd |> Result.map EmployeeEnvelope.get
      | LimitDailyDebits cmd ->
         LimitDailyDebitsCommand.toEvent cmd |> Result.map EmployeeEnvelope.get
      | LockCard cmd ->
         LockCardCommand.toEvent cmd |> Result.map EmployeeEnvelope.get
      | UnlockCard cmd ->
         UnlockCardCommand.toEvent cmd |> Result.map EmployeeEnvelope.get
      | UpdateRole cmd ->
         UpdateRoleCommand.toEvent cmd |> Result.map EmployeeEnvelope.get
      | CancelInvitation cmd ->
         CancelInvitationCommand.toEvent cmd |> Result.map EmployeeEnvelope.get
      | RefreshInvitationToken cmd ->
         RefreshInvitationTokenCommand.toEvent cmd
         |> Result.map EmployeeEnvelope.get
      | ConfirmInvitation cmd ->
         ConfirmInvitationCommand.toEvent cmd |> Result.map EmployeeEnvelope.get
      | RestoreAccess cmd ->
         RestoreAccessCommand.toEvent cmd |> Result.map EmployeeEnvelope.get
      | cmd ->
         Error
         <| ValidationErrors.create "" [
            $"API-based command processing not allowed for {cmd}"
         ]

   let! res = validation |> Result.mapError Err.ValidationError

   let actorRef =
      EmployeeActor.get system (EmployeeId.fromEntityId res.EntityId)

   actorRef <! EmployeeMessage.StateChange command
   return res
}

let getEmployee (id: EmployeeId) =
   pgQuerySingle<Employee>
      $"SELECT * FROM {table}
        WHERE {Fields.employeeId} = @id"
      (Some [ "id", Writer.employeeId id ])
      Reader.employee

let employeeReadyForInviteConfirmation
   (em: Employee)
   : (Employee * InviteToken) option
   =
   match em.Status with
   | EmployeeStatus.PendingInviteConfirmation token when not (token.IsExpired()) ->
      Some(em, token)
   | _ -> None

let getEmployeeByInviteToken (token: Guid) = taskResult {
   let! employee =
      pgQuerySingle<Employee>
         $"SELECT * FROM {table}
           WHERE {Fields.inviteToken} = @inviteToken"
         (Some [ "inviteToken", Writer.inviteToken (Some token) ])
         Reader.employee

   return Option.bind employeeReadyForInviteConfirmation employee
}

let getEmployeeByEmail (email: Email) =
   pgQuerySingle<Employee>
      $"SELECT * FROM {table}
        WHERE {Fields.email} = @email"
      (Some [ "email", Writer.email email ])
      Reader.employee

let getEmployeeInviteByEmail (email: Email) = taskResult {
   let! employee = getEmployeeByEmail email
   return Option.bind employeeReadyForInviteConfirmation employee
}

let getEmployees (orgId: OrgId) (query: EmployeeQuery) =
   let agg = [ "orgId", Writer.orgId orgId ], $"{Fields.orgId} = @orgId"

   let agg =
      Option.fold
         (fun (queryParams, where) employeeIds ->
            [ "eIds", Writer.employeeIds employeeIds ] @ queryParams,
            $"{where} AND {Fields.employeeId} = ANY(@eIds)")
         agg
         query.EmployeeIds

   let agg =
      Option.fold
         (fun (queryParams, where) roles ->
            [ "roles", Writer.roles roles ] @ queryParams,
            $"{where} AND {Fields.role} = ANY(SELECT UNNEST(@roles)::{EmployeeSqlMapper.EmployeeTypeCast.role})")
         agg
         query.Roles

   let queryParams, where = agg

   let query =
      $"SELECT * FROM {table}
        WHERE {where}
        ORDER BY {Fields.firstName}"

   pgQuery<Employee> query (Some queryParams) Reader.employee

module Fields = EmployeeEventSqlMapper.EmployeeEventFields
module Reader = EmployeeEventSqlMapper.EmployeeEventSqlReader
module Writer = EmployeeEventSqlMapper.EmployeeEventSqlWriter

let filtersToEventNames
   (filters: EmployeeEventGroupFilter list)
   : string array
   =
   filters
   |> List.fold
      (fun acc e ->
         acc
         @ match e with
           | EmployeeEventGroupFilter.Invitation -> [
              typeof<CreatedEmployee>.Name
              typeof<InvitationConfirmed>.Name
              typeof<InvitationDenied>.Name
              typeof<InvitationCancelled>.Name
             ]
           | EmployeeEventGroupFilter.CardFrozenUnfrozen -> [
              typeof<LockedCard>.Name
              typeof<UnlockedCard>.Name
             ]
           | EmployeeEventGroupFilter.AccessRestored -> [
              typeof<AccessRestored>.Name
             ]
           | EmployeeEventGroupFilter.Purchase -> [
              typeof<DebitRequested>.Name
              typeof<DebitApproved>.Name
              typeof<DebitDeclined>.Name
             ]
           | EmployeeEventGroupFilter.CreatedCard -> [
              typeof<CreatedCard>.Name
             ]
           | EmployeeEventGroupFilter.UpdatedRole -> [
              typeof<RoleUpdated>.Name
             ]
           | EmployeeEventGroupFilter.DailyDebitLimitUpdated -> [
              typeof<DailyDebitLimitUpdated>.Name
             ])
      []
   |> List.toArray

let getEmployeeHistory
   (orgId: OrgId)
   (query: Lib.NetworkQuery.EmployeeHistoryQuery)
   =
   let employeeTable = EmployeeSqlMapper.table
   let eventTable = EmployeeEventSqlMapper.table
   let limit = 10

   let agg =
      [
         "orgId", Writer.orgId orgId
         "offset", Sql.int <| Math.Max(query.Page - 1, 0) * limit
      ],
      $"{eventTable}.{Fields.orgId} = @orgId"

   let queryParams, where = agg

   let agg =
      match query.EmployeeIds, query.InitiatedByIds with
      | Some employeeIds, Some initiatedByIds ->
         [
            "eIds", Writer.employeeIds employeeIds
            "iIds", Writer.initiatedByIds initiatedByIds
         ]
         @ queryParams,
         $"{where} AND (
            {eventTable}.{Fields.employeeId} = ANY(@eIds)
            OR {eventTable}.{Fields.initiatedById} = ANY(@iIds) 
         )"
      | Some employeeIds, None ->
         [ "eIds", Writer.employeeIds employeeIds ] @ queryParams,
         $"{where} AND {eventTable}.{Fields.employeeId} = ANY(@eIds)"
      | None, Some initiatedByIds ->
         [ "iIds", Writer.initiatedByIds initiatedByIds ] @ queryParams,
         $"{where} AND {eventTable}.{Fields.initiatedById} = ANY(@iIds)"
      | None, None -> agg

   let agg =
      Option.fold
         (fun (queryParams, where) (startDate, endDate) ->
            [
               "start", Writer.timestamp startDate
               "end", Writer.timestamp endDate
            ]
            @ queryParams,
            $"{where} AND {Fields.timestamp} >= @start 
                      AND {Fields.timestamp} <= @end")
         agg
         query.DateRange

   let agg =
      Option.fold
         (fun (queryParams, where) filters ->
            [ "eventTypes", filters |> filtersToEventNames |> Sql.stringArray ]
            @ queryParams,
            $"{where} AND {Fields.name} = ANY(@eventTypes)")
         agg
         query.EventType

   let queryParams, where = agg

   let query =
      $"SELECT
           {Fields.event},
           e1.{Fields.firstName} || ' ' || e1.{Fields.lastName} as name,
           e2.{Fields.firstName} || ' ' || e2.{Fields.lastName} as initiator
        FROM {eventTable}
        LEFT JOIN {employeeTable} e1 using({Fields.employeeId})
        LEFT JOIN {employeeTable} e2 
           ON {eventTable}.{Fields.initiatedById} = e2.{Fields.employeeId}
        WHERE {where}
        ORDER BY {eventTable}.timestamp desc
        LIMIT {limit}
        OFFSET @offset"

   pgQuery<EmployeeHistory> query (Some queryParams) (fun read -> {
      InitiatedByName = read.string "initiator"
      EmployeeName = read.string "name"
      Event = Reader.event read
   })
