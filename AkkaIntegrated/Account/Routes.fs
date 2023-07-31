module Bank.Account.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Akka.Actor

open Lib.Types
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

let startAccountRoutes (app: WebApplication) =
   app.MapGet(
      Path.Base,
      Func<ActorSystem, Task<IResult>>(fun actorSystem ->
         getAccountCreatedEvents actorSystem |> RouteUtil.unwrapTaskOption)
   )
   |> ignore

   app.MapGet(
      Path.Account,
      Func<ActorSystem, Guid, Task<IResult>>(fun actorSystem id ->
         getAccount (getAccountEvents actorSystem) id
         |> RouteUtil.unwrapTaskOption)
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
            createAccount fac (Validators.accountCreate ()) command
            |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore

   app.MapDelete(
      Path.AccountEvents,
      Func<AccountActorFac, Guid, Task<IResult>>(fun fac id ->
         softDeleteEvents fac id |> RouteUtil.unwrapTask)
   )
   |> ignore

   app.MapPost(
      Path.Deposit,
      Func<AccountActorFac, DepositCashCommand, Task<IResult>>
         (fun fac command ->
            processCommand fac (Validators.deposit ()) command
            |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore

   app.MapPost(
      Path.Debit,
      Func<AccountActorFac, DebitCommand, Task<IResult>>(fun fac command ->
         processCommand fac (Validators.debit ()) command
         |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore

   app.MapPost(
      Path.DailyDebitLimit,
      Func<AccountActorFac, LimitDailyDebitsCommand, Task<IResult>>
         (fun fac command ->
            processCommand fac (Validators.dailyDebitLimit ()) command
            |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore

   app.MapPost(
      Path.LockCard,
      Func<AccountActorFac, LockCardCommand, Task<IResult>>(fun fac command ->
         processCommand fac (PassValidation()) command
         |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore

   app.MapPost(
      Path.UnlockCard,
      Func<AccountActorFac, UnlockCardCommand, Task<IResult>>(fun fac command ->
         processCommand fac (PassValidation()) command
         |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore