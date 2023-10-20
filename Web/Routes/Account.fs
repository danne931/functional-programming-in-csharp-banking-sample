module Bank.Account.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Akka.Actor

open Bank.Account.Domain
open Bank.Account.Api

module private Path =
   let Base = "/accounts"
   let Account = Base + "/{id}"
   let Diagnostic = "/diagnostic"
   let AccountEvents = Diagnostic + "/events/{id}"
   let Deposit = Base + "/deposit"
   let Debit = Base + "/debit"
   let DailyDebitLimit = Base + "/daily-debit-limit"
   let LockCard = Base + "/lock"
   let UnlockCard = Base + "/unlock"
   let CloseAccount = Base + "/close-account"

let startAccountRoutes (app: WebApplication) =
   app.MapGet(
      Path.Account,
      Func<ActorSystem, Guid, Task<IResult>>(fun system id ->
         getAccount system id |> RouteUtil.unwrapTaskOption)
   )
   |> ignore

   app.MapGet(
      Path.AccountEvents,
      Func<ActorSystem, Guid, Task<IResult>>(fun actorSystem id ->
         getAccountEvents actorSystem id |> RouteUtil.unwrapTaskOption)
   )
   |> ignore

   app.MapPost(
      Path.Base,
      Func<ActorSystem, CreateAccountCommand, Task<IResult>>(fun system cmd ->
         cmd.toEvent ()
         |> processCommand system cmd
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.Deposit,
      Func<ActorSystem, DepositCashCommand, Task<IResult>>(fun sys cmd ->
         cmd.toEvent () |> processCommand sys cmd |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.Debit,
      Func<ActorSystem, DebitCommand, Task<IResult>>(fun sys cmd ->
         cmd.toEvent () |> processCommand sys cmd |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.DailyDebitLimit,
      Func<ActorSystem, LimitDailyDebitsCommand, Task<IResult>>(fun sys cmd ->
         cmd.toEvent () |> processCommand sys cmd |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.LockCard,
      Func<ActorSystem, LockCardCommand, Task<IResult>>(fun sys cmd ->
         cmd.toEvent () |> processCommand sys cmd |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.UnlockCard,
      Func<ActorSystem, UnlockCardCommand, Task<IResult>>(fun sys cmd ->
         cmd.toEvent () |> processCommand sys cmd |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.CloseAccount,
      Func<ActorSystem, CloseAccountCommand, Task<IResult>>(fun sys cmd ->
         cmd.toEvent () |> processCommand sys cmd |> RouteUtil.unwrapTaskResult)
   )
   |> ignore
