module EmployeeSqlMapper

open System

open Lib.SharedTypes
open Bank.Employee.Domain
open OrganizationSqlMapper

let table = "employee"

module EmployeeTypeCast =
   let status = "employee_status"

module EmployeeFields =
   let employeeId = "employee_id"
   let orgId = OrgFields.orgId
   let role = "role"
   let email = "email"
   let firstName = "first_name"
   let lastName = "last_name"
   let cards = "cards"
   let status = "status"
   let pendingPurchases = "pending_purchases"
   let searchQuery = "search_query"

module EmployeeSqlReader =
   let employeeId (read: RowReader) =
      EmployeeFields.employeeId |> read.uuid |> EmployeeId

   let orgId = OrgSqlReader.orgId

   let role (read: RowReader) =
      read.string EmployeeFields.role
      |> sprintf "\"%s\""
      |> Serialization.deserializeUnsafe<EmployeeRole>

   let email (read: RowReader) =
      read.string EmployeeFields.email |> Email.deserialize

   let firstName (read: RowReader) = read.string EmployeeFields.firstName
   let lastName (read: RowReader) = read.string EmployeeFields.lastName
   let searchQuery (read: RowReader) = read.text EmployeeFields.searchQuery

   let cards (read: RowReader) =
      read.text EmployeeFields.cards
      |> Serialization.deserializeUnsafe<Card list>

   let status (read: RowReader) =
      read.string EmployeeFields.status |> EmployeeStatus.fromStringUnsafe

   let pendingPurchases (read: RowReader) =
      read.text EmployeeFields.pendingPurchases
      |> Serialization.deserializeUnsafe<DebitInfo list>

   let employee (read: RowReader) : Employee = {
      EmployeeId = employeeId read
      OrgId = orgId read
      Role = role read
      Email = email read
      FirstName = firstName read
      LastName = lastName read
      Cards = cards read |> List.map (fun o -> o.CardId, o) |> Map.ofList
      Status = status read
      PendingPurchases =
         pendingPurchases read
         |> List.map (fun o -> o.CorrelationId, o)
         |> Map.ofList
   }

module EmployeeSqlWriter =
   let employeeId = EmployeeId.get >> Sql.uuid
   let orgId = OrgSqlWriter.orgId
   let role (role: EmployeeRole) = Sql.string <| string role
   let email (email: Email) = Sql.string <| string email
   let firstName = Sql.string
   let lastName = Sql.string
   let searchQuery = Sql.text

   let cards (cards: Map<CardId, Card>) =
      cards.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let status (status: EmployeeStatus) =
      status |> string |> _.ToLower() |> Sql.string

   let pendingPurchases (pendingPurchases: Map<CorrelationId, DebitInfo>) =
      pendingPurchases.Values
      |> Seq.toList
      |> Serialization.serialize
      |> Sql.jsonb
