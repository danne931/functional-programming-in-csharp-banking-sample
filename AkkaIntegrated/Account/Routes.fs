module Bank.Account.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Akkling
open Akka.Actor

open Lib.Types
open BankTypes
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
         getAccountIds actorSystem |> RouteUtil.unwrapTaskOption)
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
      Func<IActorRef<AccountCoordinatorMessage>, CreateAccountCommand, Task<IResult>>
         (fun coordinator command ->
            createAccount coordinator (Validators.accountCreate ()) command
            |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore

   app.MapDelete(
      Path.AccountEvents,
      Func<IActorRef<AccountCoordinatorMessage>, Guid, Task<IResult>>
         (fun coordinator id ->
            softDeleteEvents coordinator id |> RouteUtil.unwrapTask)
   )
   |> ignore

   app.MapPost(
      Path.Deposit,
      Func<IActorRef<AccountCoordinatorMessage>, DepositCashCommand, Task<IResult>>
         (fun coordinator command ->
            processCommand coordinator (Validators.deposit ()) command
            |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore

   app.MapPost(
      Path.Debit,
      Func<IActorRef<AccountCoordinatorMessage>, DebitCommand, Task<IResult>>
         (fun coordinator command ->
            processCommand coordinator (Validators.debit ()) command
            |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore

   app.MapPost(
      Path.DailyDebitLimit,
      Func<IActorRef<AccountCoordinatorMessage>, LimitDailyDebitsCommand, Task<IResult>>
         (fun coordinator command ->
            processCommand coordinator (Validators.dailyDebitLimit ()) command
            |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore

   app.MapPost(
      Path.LockCard,
      Func<IActorRef<AccountCoordinatorMessage>, LockCardCommand, Task<IResult>>
         (fun coordinator command ->
            processCommand coordinator (PassValidation()) command
            |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore

   app.MapPost(
      Path.UnlockCard,
      Func<IActorRef<AccountCoordinatorMessage>, UnlockCardCommand, Task<IResult>>
         (fun coordinator command ->
            processCommand coordinator (PassValidation()) command
            |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore
