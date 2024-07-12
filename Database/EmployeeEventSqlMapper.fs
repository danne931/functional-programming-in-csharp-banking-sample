module EmployeeEventSqlMapper

open System

open Bank.Employee.Domain
open Lib.SharedTypes
open EmployeeSqlMapper
open OrganizationSqlMapper

let table = "employee_event"

module EmployeeEventFields =
   let eventId = "event_id"
   let orgId = OrgFields.orgId
   let employeeId = EmployeeFields.employeeId
   let correlationId = "correlation_id"
   let initiatedById = "initiated_by_id"
   let name = "name"
   let timestamp = "timestamp"
   let event = "event"

module EmployeeEventSqlReader =
   let eventId (read: RowReader) =
      EmployeeEventFields.eventId |> read.uuid |> EventId

   let orgId = OrgSqlReader.orgId
   let employeeId = EmployeeSqlReader.employeeId

   let correlationId (read: RowReader) =
      EmployeeEventFields.correlationId |> read.uuid |> CorrelationId

   let initiatedById (read: RowReader) =
      EmployeeEventFields.initiatedById
      |> read.uuid
      |> EmployeeId
      |> InitiatedById

   let name (read: RowReader) = read.text EmployeeEventFields.name

   let timestamp (read: RowReader) =
      read.dateTime EmployeeEventFields.timestamp

   let event (read: RowReader) =
      read.text EmployeeEventFields.event
      |> Serialization.deserializeUnsafe<EmployeeEvent>

module EmployeeEventSqlWriter =
   let eventId (evtId: EventId) =
      let (EventId id) = evtId
      Sql.uuid id

   let correlationId (corrId: CorrelationId) =
      let (CorrelationId id) = corrId
      Sql.uuid id

   let initiatedById (id: InitiatedById) = id |> InitiatedById.get |> Sql.uuid

   let initiatedByIds (ids: InitiatedById list) =
      ids |> List.map InitiatedById.get |> List.toArray |> Sql.uuidArray

   let orgId = OrgSqlWriter.orgId
   let employeeId = EmployeeSqlWriter.employeeId
   let employeeIds = EmployeeSqlWriter.employeeIds
   let name = Sql.text
   let timestamp (date: DateTime) = Sql.timestamptz date

   let event (evt: EmployeeEvent) =
      Sql.jsonb <| Serialization.serialize evt
