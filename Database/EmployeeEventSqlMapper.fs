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
   let name = "name"
   let timestamp = "timestamp"
   let event = "event"

module EmployeeEventSqlReader =
   let eventId (read: RowReader) =
      EmployeeEventFields.eventId |> read.uuid |> EventId

   let orgId = OrgSqlReader.orgId
   let employeeId = EmployeeSqlReader.employeeId

   let correlationId (read: RowReader) =
      EmployeeFields.employeeId |> read.uuid |> CorrelationId

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

   let orgId = OrgSqlWriter.orgId
   let employeeId = EmployeeSqlWriter.employeeId
   let name = Sql.text
   let timestamp (date: DateTime) = Sql.timestamptz date

   let event (evt: EmployeeEvent) =
      Sql.jsonb <| Serialization.serialize evt
