module Bank.Employee.Api

open Akkling
open Akka.Actor
open FsToolkit.ErrorHandling
open Validus

open Lib.Postgres
open Lib.SharedTypes
open Bank.Employee.Domain

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
   let ids (cmd: BankEvent<_>) : CommandProcessingResponse = {
      EntityId = cmd.EntityId
      CorrelationId = cmd.CorrelationId
      EventId = cmd.Id
   }

   let validation =
      match command with
      | CreateEmployee cmd ->
         CreateEmployeeCommand.toEvent cmd |> Result.map ids
      | DebitRequest cmd -> DebitRequestCommand.toEvent cmd |> Result.map ids
      | LimitDailyDebits cmd ->
         LimitDailyDebitsCommand.toEvent cmd |> Result.map ids
      | LockCard cmd -> LockCardCommand.toEvent cmd |> Result.map ids
      | UnlockCard cmd -> UnlockCardCommand.toEvent cmd |> Result.map ids
      | cmd ->
         Error
         <| ValidationErrors.create "" [
            $"Command processing not implemented for {cmd}"
         ]

   let! res = validation |> Result.mapError Err.ValidationError

   let actorRef =
      EmployeeActor.get system (EmployeeId.fromEntityId res.EntityId)

   actorRef <! EmployeeMessage.StateChange command
   return validation
}

let getEmployee (id: EmployeeId) =
   pgQuerySingle<Employee>
      $"SELECT * FROM {table} 
        WHERE {Fields.employeeId} = @id"
      (Some [ "id", Writer.employeeId id ])
      Reader.employee

let getEmployees (orgId: OrgId) =
   pgQuery<Employee>
      $"SELECT * FROM {table} 
        WHERE {Fields.orgId} = @orgId
        ORDER BY {Fields.firstName}"
      (Some [ "orgId", Writer.orgId orgId ])
      Reader.employee
