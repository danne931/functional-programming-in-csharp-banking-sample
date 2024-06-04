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

let startAccountRoutes (app: WebApplication) =
   app.MapGet(
      AccountPath.Base,
      Func<Guid, Task<IResult>>(fun ([<FromQuery>] orgId) ->
         getAccountProfiles (OrgId orgId) |> RouteUtil.unwrapTaskResultOption)
   )
   |> ignore

   app.MapGet(
      AccountPath.Account,
      Func<Guid, Task<IResult>>(fun id ->
         getAccount (AccountId id) |> RouteUtil.unwrapTaskResultOption)
   )
   |> ignore

   app.MapGet(
      AccountPath.AccountAndTransactions,
      Bank.Transaction.Routes.withQueryParams<Task<IResult>> (
         getAccountAndTransactions >> RouteUtil.unwrapTaskResultOption
      )
   )
   |> ignore

   app.MapPost(
      AccountPath.Base,
      Func<ActorSystem, CreateAccountCommand, Task<IResult>>(fun system cmd ->
         processCommand system (AccountCommand.CreateAccount cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      AccountPath.Deposit,
      Func<ActorSystem, DepositCashCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (AccountCommand.DepositCash cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      AccountPath.Debit,
      Func<ActorSystem, DebitCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (AccountCommand.Debit cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      AccountPath.DailyDebitLimit,
      Func<ActorSystem, LimitDailyDebitsCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (AccountCommand.LimitDailyDebits cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      AccountPath.LockCard,
      Func<ActorSystem, LockCardCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (AccountCommand.LockCard cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      AccountPath.UnlockCard,
      Func<ActorSystem, UnlockCardCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (AccountCommand.UnlockCard cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      AccountPath.CloseAccount,
      Func<ActorSystem, CloseAccountCommand, Task<IResult>>(fun sys cmd ->
         processCommand sys (AccountCommand.CloseAccount cmd)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapGet(
      AccountPath.BillingStatement,
      Func<Guid, int, Task<IResult>>(fun accountId page ->
         getBillingTransactions (AccountId accountId) page
         |> RouteUtil.unwrapTaskResultOption)
   )
   |> ignore
