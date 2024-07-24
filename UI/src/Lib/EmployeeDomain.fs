module UIDomain.Employee

open Bank.Employee.Domain
open Lib.SharedTypes
open Lib.NetworkQuery

type EmployeesMaybe = Result<Map<EmployeeId, Employee> option, Err>

type EmployeeHistoryMaybe = Result<EmployeeHistory list option, Err>

type EmployeeCommandReceipt = {
   PendingCommand: EmployeeCommand
   PendingEvent: EmployeeEvent
   PendingState: Employee
   Envelope: Envelope
}

type EmployeeHistoryUIFriendly = {
   Name: string
   Date: string
   Amount: string
   Info: string
   MoneyFlow: MoneyFlow option
   Initiator: string
   EmployeeName: string
}

let employeeEventUIFriendly (txn: EmployeeHistory) : EmployeeHistoryUIFriendly =
   let _, envelope = EmployeeEnvelope.unwrap txn.Event

   let props = {
      Date = dateUIFriendly envelope.Timestamp
      Name = ""
      Amount = "-"
      MoneyFlow = None
      Initiator = txn.InitiatedByName
      EmployeeName = txn.EmployeeName
      Info = ""
   }

   match txn.Event with
   | EmployeeEvent.DebitRequested e -> {
      props with
         Name = "Purchase Requested"
         Info =
            $"Purchase requested by {txn.EmployeeName} at {e.Data.Info.Origin} with card {e.Data.Info.CardNumberLast4}"
         Amount = Money.format e.Data.Info.Amount
     }
   | EmployeeEvent.DebitApproved e -> {
      props with
         Name = "Purchase Approved"
         Info =
            $"Purchase approved for {txn.EmployeeName} at {e.Data.Info.Origin} with card {e.Data.Info.CardNumberLast4}"
         Amount = Money.format e.Data.Info.Amount
         MoneyFlow = Some MoneyFlow.Out
     }
   | EmployeeEvent.DebitDeclined e -> {
      props with
         Name = "Purchase Declined"
         Info =
            $"Purchase declined for {txn.EmployeeName} at {e.Data.Info.Origin} with card {e.Data.Info.CardNumberLast4}"
         Amount = Money.format e.Data.Info.Amount
     }
   | EmployeeEvent.CreatedEmployee e -> {
      props with
         Name = "Employee Created"
         Info =
            $"Created employee {txn.EmployeeName} with role {e.Data.Role.Display}"
     }
   | EmployeeEvent.CreatedCard e -> {
      props with
         Name = "Card Created"
         Info =
            $"Created card **{e.Data.Card.CardNumberLast4} for {txn.EmployeeName}"
     }
   | EmployeeEvent.CreatedAccountOwner e -> {
      props with
         Name = "Account Owner Created"
         Info = $"Created account owner {txn.EmployeeName}"
     }
   | EmployeeEvent.UpdatedRole e -> {
      props with
         Name = "Updated Role"
         Info =
            $"Updated role of {txn.EmployeeName} from {e.Data.PriorRole.Display} to {e.Data.Role.Display}"
     }
   | EmployeeEvent.LockedCard e -> {
      props with
         Name = "Locked Card"
         Info = $"Locked card {e.Data.CardNumberLast4} for {txn.EmployeeName}"
     }
   | EmployeeEvent.UnlockedCard e -> {
      props with
         Name = "Unlocked Card"
         Info = $"Unlocked card {e.Data.CardNumberLast4} for {txn.EmployeeName}"
     }
   | EmployeeEvent.AccessRestored e -> {
      props with
         Name = "Access Restored"
         Info = $"Employee access restored for {txn.EmployeeName}"
     }
   | EmployeeEvent.DailyDebitLimitUpdated e -> {
      props with
         Name = "Daily Purchase Limit Updated"
         Info =
            $"Updated daily purchase limit from ${e.Data.PriorLimit} to ${e.Data.DebitLimit} for {txn.EmployeeName}'s card **{e.Data.CardNumberLast4}"
     }
   | EmployeeEvent.MonthlyDebitLimitUpdated e -> {
      props with
         Name = "Monthly Purchase Limit Updated"
         Info =
            $"Updated monthly purchase limit from ${e.Data.PriorLimit} to ${e.Data.DebitLimit} for {txn.EmployeeName}'s card **{e.Data.CardNumberLast4}"
     }
   | EmployeeEvent.CardNicknamed e -> {
      props with
         Name = "Card Nickname Updated"
         Info =
            $"Card nickname updated from {e.Data.PriorName} to {e.Data.Name} for {txn.EmployeeName}'s card"
     }
   | EmployeeEvent.InvitationDenied e -> {
      props with
         Name = "Invitation Denied"
         Info = "Invitation denied"
     }
   | EmployeeEvent.InvitationApproved e ->
      let approvers = e.Data.Approvers

      {
         props with
            Name = "Invitation Approved"
            Info = $"Invitation approved by: {approvers}"
      }
   | EmployeeEvent.InvitationConfirmed e -> {
      props with
         Name = "Invitation Confirmed"
         Info = "Invitation confirmed"
     }
   | EmployeeEvent.InvitationCancelled e -> {
      props with
         Name = "Invitation Cancelled"
         Info = "Invitation cancelled"
     }
   | EmployeeEvent.InvitationTokenRefreshed e -> {
      props with
         Name = "Invitation Token Refreshed"
         Info = "Invitation token refreshed"
     }

type SelectedEmployee = {
   Id: EmployeeId
   Name: string
   Email: string
}

[<RequireQualifiedAccess>]
type EmployeeActionView =
   | Create
   | ViewEmployee of EmployeeId

type EmployeeBrowserQuery = {
   Action: EmployeeActionView option
   SelectedEmployees: (SelectedEmployee list) option
   Roles: (Role list) option
} with

   member x.ChangeDetection =
      Serialization.serialize {|
         Roles = x.Roles
         SelectedEmployees = x.SelectedEmployees
      |}

let parseEmployees =
   Serialization.deserialize<SelectedEmployee list> >> Result.toOption

module EmployeeBrowserQuery =
   let toQueryParams (query: EmployeeBrowserQuery) : (string * string) list =
      let agg = []

      let agg =
         match query.Action with
         | Some view -> ("action", string view) :: agg
         | None -> agg

      let agg =
         match query.SelectedEmployees with
         | None -> agg
         | Some employees ->
            ("employees", Serialization.serialize employees) :: agg

      let agg =
         match query.Roles with
         | Some roles -> ("roles", listToQueryString roles) :: agg
         | None -> agg

      agg

   let private (|ViewEmployee|_|) (input: string) =
      let needle = "ViewEmployee "

      if input.StartsWith(needle) then
         input.Substring(needle.Length)
         |> Guid.parseOptional
         |> Option.map EmployeeId
      else
         None

   let fromQueryParams
      (queryParams: (string * string) list)
      : EmployeeBrowserQuery
      =
      let queryParams = Map.ofList queryParams

      {
         Action =
            Map.tryFind "action" queryParams
            |> Option.bind (function
               | "Create" -> Some EmployeeActionView.Create
               | ViewEmployee id -> Some(EmployeeActionView.ViewEmployee id)
               | view ->
                  Log.error $"Employee action view not implemented: {view}"
                  None)
         Roles =
            Map.tryFind "roles" queryParams
            |> Option.bind EmployeeQuery.rolesFromQueryString
         SelectedEmployees =
            Map.tryFind "employees" queryParams |> Option.bind parseEmployees
      }

   let empty: EmployeeBrowserQuery = {
      Action = None
      Roles = None
      SelectedEmployees = None
   }

type EmployeeHistoryBrowserQuery = {
   Date: DateFilter option
   EventType: (EmployeeEventGroupFilter list) option
   SelectedEmployees: (SelectedEmployee list) option
   SelectedInitiatedBy: (SelectedEmployee list) option
}

module EmployeeHistoryBrowserQuery =
   let toQueryParams
      (query: EmployeeHistoryBrowserQuery)
      : (string * string) list
      =
      let agg = []

      let agg =
         match query.SelectedEmployees with
         | None -> agg
         | Some employees ->
            ("employees", Serialization.serialize employees) :: agg

      let agg =
         match query.SelectedInitiatedBy with
         | None -> agg
         | Some initiatedBy ->
            ("initiatedBy", Serialization.serialize initiatedBy) :: agg

      let agg =
         match query.EventType with
         | None -> agg
         | Some filters -> ("events", listToQueryString filters) :: agg

      // If custom date range selected, date query param will consist
      // of a start & end date.  Otherwise it will be something like
      // date=Last30Days; date=LastMonth; etc.
      let agg =
         match query.Date with
         | None -> agg
         | Some(DateFilter.Custom(startDate, endDate)) ->
            ("date", DateTime.rangeAsQueryString startDate endDate) :: agg
         | Some filter -> ("date", string filter) :: agg

      agg

   let fromQueryParams
      (queryParams: (string * string) list)
      : EmployeeHistoryBrowserQuery
      =
      let queryParams = Map.ofList queryParams

      {
         Date =
            Map.tryFind "date" queryParams |> Option.bind DateFilter.fromString
         EventType =
            Map.tryFind "events" queryParams
            |> Option.bind EmployeeEventGroupFilter.fromQueryString
         SelectedEmployees =
            Map.tryFind "employees" queryParams |> Option.bind parseEmployees
         SelectedInitiatedBy =
            Map.tryFind "initiatedBy" queryParams |> Option.bind parseEmployees
      }

   let empty: EmployeeHistoryBrowserQuery = {
      Date = None
      EventType = None
      SelectedEmployees = None
      SelectedInitiatedBy = None
   }
