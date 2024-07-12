module EmployeeSqlMapper

open System

open Lib.SharedTypes
open Bank.Employee.Domain
open OrganizationSqlMapper

let table = "employee"

module EmployeeTypeCast =
   let status = "employee_status"
   let role = "employee_role"

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
   let onboardingTasks = "onboarding_tasks"
   let searchQuery = "search_query"
   let inviteToken = "invite_token"
   let inviteExpiration = "invite_expiration"
   let authProviderUserId = "auth_provider_user_id"

module EmployeeSqlReader =
   let employeeId (read: RowReader) =
      EmployeeFields.employeeId |> read.uuid |> EmployeeId

   let orgId = OrgSqlReader.orgId

   let role (read: RowReader) =
      read.string EmployeeFields.role |> Role.fromStringUnsafe

   let email (read: RowReader) =
      read.string EmployeeFields.email |> Email.deserialize

   let firstName (read: RowReader) = read.string EmployeeFields.firstName
   let lastName (read: RowReader) = read.string EmployeeFields.lastName
   let searchQuery (read: RowReader) = read.text EmployeeFields.searchQuery

   let cards (read: RowReader) =
      read.text EmployeeFields.cards
      |> Serialization.deserializeUnsafe<Card list>

   let inviteToken (read: RowReader) : InviteToken option =
      let token = read.uuidOrNone EmployeeFields.inviteToken
      let exp = read.dateTimeOrNone EmployeeFields.inviteExpiration

      Option.map2
         (fun token exp -> { Token = token; Expiration = exp })
         token
         exp

   let status (read: RowReader) =
      let status = read.string EmployeeFields.status

      match status with
      | "active" -> EmployeeStatus.Active
      | "closed" -> EmployeeStatus.Closed
      | "pendingrestoreaccessapproval" ->
         EmployeeStatus.PendingRestoreAccessApproval
      | "readyfordelete" -> EmployeeStatus.ReadyForDelete
      | "pendinginviteapproval" -> EmployeeStatus.PendingInviteApproval
      | "pendinginviteconfirmation" ->
         match inviteToken read with
         | Some token -> EmployeeStatus.PendingInviteConfirmation token
         | None ->
            failwith
               "Employee should not have status PendingInviteInvite without a token"
      | _ -> failwith "Error attempting to read EmployeeStatus"

   let pendingPurchases (read: RowReader) =
      read.text EmployeeFields.pendingPurchases
      |> Serialization.deserializeUnsafe<DebitInfo list>

   let onboardingTasks (read: RowReader) =
      read.text EmployeeFields.onboardingTasks
      |> Serialization.deserializeUnsafe<EmployeeOnboardingTask list>

   let authProviderUserId (read: RowReader) =
      read.uuidOrNone EmployeeFields.authProviderUserId

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
      OnboardingTasks = onboardingTasks read
      AuthProviderUserId = authProviderUserId read
   }

module EmployeeSqlWriter =
   let employeeId = EmployeeId.get >> Sql.uuid

   let employeeIds (ids: EmployeeId list) =
      ids |> List.map EmployeeId.get |> Array.ofList |> Sql.uuidArray

   let orgId = OrgSqlWriter.orgId
   let role (role: Role) = Sql.string <| string role

   let roles (roles: Role list) =
      roles |> List.map string |> List.toArray |> Sql.stringArray

   let email (email: Email) = Sql.string <| string email
   let firstName = Sql.string
   let lastName = Sql.string
   let searchQuery = Sql.text

   let cards (cards: Map<CardId, Card>) =
      cards.Values |> Seq.toList |> Serialization.serialize |> Sql.jsonb

   let status (status: EmployeeStatus) = status |> string |> Sql.string

   let pendingPurchases (pendingPurchases: Map<CorrelationId, DebitInfo>) =
      pendingPurchases.Values
      |> Seq.toList
      |> Serialization.serialize
      |> Sql.jsonb

   let onboardingTasks (tasks: EmployeeOnboardingTask list) =
      tasks |> Serialization.serialize |> Sql.jsonb

   let inviteToken = Sql.uuidOrNone
   let inviteExpiration (date: DateTime option) = Sql.timestamptzOrNone date

   let inviteTokenFromStatus (status: EmployeeStatus) =
      match status with
      | EmployeeStatus.PendingInviteConfirmation token ->
         inviteToken (Some token.Token)
      | _ -> inviteToken None

   let inviteExpirationFromStatus (status: EmployeeStatus) =
      match status with
      | EmployeeStatus.PendingInviteConfirmation token ->
         inviteExpiration (Some token.Expiration)
      | _ -> inviteExpiration None

   let authProviderUserId = Sql.uuidOrNone
