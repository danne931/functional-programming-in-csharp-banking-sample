module Bank.Account.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Akka.Actor

open ActorUtil
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
      Func<AccountActorFac, Guid, Task<IResult>>(fun fac id ->
         getAccount fac id |> RouteUtil.unwrapTaskOption)
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
      Func<AccountActorFac, CreateAccountCommand, Task<IResult>>
         (fun fac command ->
            createAccount fac command |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapDelete(
      Path.AccountEvents,
      Func<AccountActorFac, Guid, Task<IResult>>(fun fac id ->
         diagnosticDelete fac id |> RouteUtil.unwrapTask)
   )
   |> ignore

   let processCommand fac command =
      processCommand fac command |> RouteUtil.unwrapTaskResult

   app.MapPost(
      Path.Deposit,
      Func<AccountActorFac, DepositCashCommand, Task<IResult>>(processCommand)
   )
   |> ignore

   app.MapPost(
      Path.Debit,
      Func<AccountActorFac, DebitCommand, Task<IResult>>(processCommand)
   )
   |> ignore

   app.MapPost(
      Path.DailyDebitLimit,
      Func<AccountActorFac, LimitDailyDebitsCommand, Task<IResult>>(
         processCommand
      )
   )
   |> ignore

   app.MapPost(
      Path.LockCard,
      Func<AccountActorFac, LockCardCommand, Task<IResult>>(processCommand)
   )
   |> ignore

   app.MapPost(
      Path.UnlockCard,
      Func<AccountActorFac, UnlockCardCommand, Task<IResult>>(processCommand)
   )
   |> ignore

   app.MapPost(
      Path.CloseAccount,
      Func<AccountActorFac, CloseAccountCommand, Task<IResult>>(processCommand)
   )
   |> ignore
