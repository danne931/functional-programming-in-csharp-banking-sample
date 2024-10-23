module Bank.Account.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Akka.Actor

open Bank.Account.Domain
open Bank.Account.Api
open Bank.BillingCycle.Api
open RoutePaths
open Lib.SharedTypes
open Bank.UserSession.Middleware

let startAccountRoutes (app: WebApplication) =
   app
      .MapGet(
         OrgPath.Get,
         Func<Guid, Task<IResult>>(fun orgId ->
            getOrgAndAccountProfiles (OrgId orgId)
            |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.GetOrgAndAccountProfiles)
   |> ignore

   app
      .MapGet(
         OrgPath.Search,
         Func<Guid, string, Task<IResult>>(fun orgId searchQuery ->
            searchOrgTransferSocialDiscovery (OrgId orgId) searchQuery
            |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.GetOrgAndAccountProfiles)
   |> ignore

   app
      .MapGet(
         AccountPath.Account,
         Func<Guid, Task<IResult>>(fun id ->
            getAccount (AccountId id) |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.GetAccount)
   |> ignore

   app
      .MapPost(
         AccountPath.Base,
         Func<ActorSystem, CreateAccountCommand, Task<IResult>>
            (fun system cmd ->
               processCommand system (AccountCommand.CreateAccount cmd)
               |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.CreateAccount)
   |> ignore

   app
      .MapPost(
         AccountPath.Deposit,
         Func<ActorSystem, DepositCashCommand, Task<IResult>>(fun sys cmd ->
            processCommand sys (AccountCommand.DepositCash cmd)
            |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.Deposit)
   |> ignore

   app
      .MapPost(
         AccountPath.CloseAccount,
         Func<ActorSystem, CloseAccountCommand, Task<IResult>>(fun sys cmd ->
            processCommand sys (AccountCommand.CloseAccount cmd)
            |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.CloseAccount)
   |> ignore

   app
      .MapGet(
         AccountPath.BillingStatement,
         Func<Guid, int, Task<IResult>>(fun accountId page ->
            getBillingTransactions (AccountId accountId) page
            |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.BillingStatement)
   |> ignore
