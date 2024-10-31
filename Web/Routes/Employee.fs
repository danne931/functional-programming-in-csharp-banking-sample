module Bank.Employee.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Akka.Actor
open Akkling

open Bank.Employee.Domain
open Bank.Account.Api
open Bank.Employee.Api
open RoutePaths
open Lib.SharedTypes
open Bank.UserSession.Middleware
open Lib.NetworkQuery

let startEmployeeRoutes (app: WebApplication) =
   app
      .MapGet(
         EmployeePath.Get,
         Func<Guid, string, string, Task<IResult>>
            (fun orgId ([<FromQuery>] employeeIds) ([<FromQuery>] roles) ->
               let query = {
                  EmployeeIds =
                     EmployeeQuery.employeeIdsFromQueryString employeeIds
                  Roles = EmployeeQuery.rolesFromQueryString roles
               }

               getEmployees (OrgId orgId) query
               |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.GetEmployees)
   |> ignore

   app
      .MapGet(
         EmployeePath.Search,
         Func<Guid, string, Task<IResult>>(fun orgId searchQuery ->
            searchEmployees (OrgId orgId) searchQuery
            |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.GetEmployees)
   |> ignore

   app
      .MapGet(
         EmployeePath.History,
         Func<Guid, int, string, string, string, string, Task<IResult>>
            (fun
                 orgId
                 ([<FromQuery>] page)
                 ([<FromQuery>] date)
                 ([<FromQuery>] events)
                 ([<FromQuery>] employeeIds)
                 ([<FromQuery>] initiatedByIds) ->
               let query = {
                  DateRange = dateRangeFromQueryString date
                  Page = page
                  EventType = EmployeeEventGroupFilter.fromQueryString events
                  EmployeeIds =
                     EmployeeHistoryQuery.employeeIdsFromQueryString
                        employeeIds
                  InitiatedByIds =
                     EmployeeHistoryQuery.initiatedByIdsFromQueryString
                        initiatedByIds
               }

               getEmployeeHistory (OrgId orgId) query
               |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.GetEmployeeHistory)
   |> ignore

   app
      .MapPost(
         EmployeePath.Base,
         Func<ActorSystem, CreateEmployeeCommand, Task<IResult>>(fun sys cmd ->
            processCommand sys (EmployeeCommand.CreateEmployee cmd)
            |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.CreateEmployee)
   |> ignore

   app
      .MapPost(
         EmployeePath.UpdateRole,
         Func<ActorSystem, UpdateRoleCommand, Task<IResult>>(fun sys cmd ->
            processCommand sys (EmployeeCommand.UpdateRole cmd)
            |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.UpdateRole)
   |> ignore

   app
      .MapPost(
         EmployeePath.CancelEmployeeInvitation,
         Func<ActorSystem, CancelInvitationCommand, Task<IResult>>
            (fun sys cmd ->
               processCommand sys (EmployeeCommand.CancelInvitation cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.CancelEmployeeInvitation)
   |> ignore

   app
      .MapPost(
         EmployeePath.RestoreAccess,
         Func<ActorSystem, RestoreAccessCommand, Task<IResult>>(fun sys cmd ->
            processCommand sys (EmployeeCommand.RestoreAccess cmd)
            |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.RestoreEmployeeAccess)
   |> ignore

   app
      .MapPost(
         EmployeePath.ResendInviteNotification,
         Func<ActorSystem, Employee, Task<IResult>>(fun sys employee -> task {
            match employee.Status with
            | EmployeeStatus.PendingInviteConfirmation token ->
               if token.IsExpired() then
                  match! getOrg employee.OrgId with
                  | Error err -> return RouteUtil.badRequest err
                  | Ok None -> return Results.NotFound()
                  | Ok(Some org) ->
                     let cmd =
                        RefreshInvitationTokenCommand.create
                           employee.CompositeId
                           (InitiatedById
                              Constants.LOGGED_IN_EMPLOYEE_ID_REMOVE_SOON)
                           {
                              OrgRequiresEmployeeInviteApproval =
                                 org.Permissions.RequiresEmployeeInviteApproval
                              Reason = None
                           }
                        |> EmployeeCommand.RefreshInvitationToken

                     match! (processCommand sys cmd) with
                     | Ok _ -> return Results.Ok()
                     | Error e -> return RouteUtil.badRequest e
               else
                  let invite: EmailActor.EmployeeInviteEmailInfo = {
                     OrgId = employee.OrgId
                     Name = employee.Name
                     Email = employee.Email
                     Token = token
                  }

                  EmailActor.getForwarder sys
                  <! EmailActor.EmailMessage.EmployeeInvite invite

                  return Results.Ok()
            | _ ->
               let msg =
                  $"Employee status {employee.Status} not in a state to invite."

               ActorUtil.SystemLog.error sys (exn msg) msg
               //return Results.Forbid()
               return Results.NotFound()
         })
      )
      .RBAC(Permissions.ResendInviteNotification)
   |> ignore
