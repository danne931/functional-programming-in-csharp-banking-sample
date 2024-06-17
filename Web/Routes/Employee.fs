module Bank.Employee.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Akka.Actor

open Bank.Employee.Domain
open Bank.Employee.Api
open RoutePaths
open Lib.SharedTypes

let startEmployeeRoutes (app: WebApplication) =
   app.MapGet(
      EmployeePath.Get,
      Func<Guid, string, Task<IResult>>(fun orgId ([<FromQuery>] searchQuery) ->
         let get =
            if String.IsNullOrEmpty searchQuery then
               getEmployees (OrgId orgId)
            else
               searchEmployees (OrgId orgId) searchQuery

         RouteUtil.unwrapTaskResultOption get)
   )
   |> ignore

   app.MapPost(
      EmployeePath.Debit,
      Func<ActorSystem, DebitRequestCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (EmployeeCommand.DebitRequest cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      EmployeePath.DailyDebitLimit,
      Func<ActorSystem, LimitDailyDebitsCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (EmployeeCommand.LimitDailyDebits cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      EmployeePath.LockCard,
      Func<ActorSystem, LockCardCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (EmployeeCommand.LockCard cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      EmployeePath.UnlockCard,
      Func<ActorSystem, UnlockCardCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (EmployeeCommand.UnlockCard cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore
