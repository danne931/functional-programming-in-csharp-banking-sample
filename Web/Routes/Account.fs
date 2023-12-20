module Bank.Account.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Akka.Actor
open Akkling
open FSharp.Control

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
   let CircuitBreaker = Diagnostic + "/circuit-breaker"

let startAccountRoutes (app: WebApplication) =
   app.MapGet(
      Path.Account,
      Func<ActorSystem, Guid, Task<IResult>>(fun sys id ->
         getAccount sys id |> RouteUtil.unwrapTaskOption)
   )
   |> ignore

   app.MapGet(
      Path.AccountEvents,
      Func<ActorSystem, Guid, Task<IResult>>(fun sys id ->
         getAccountEvents sys id |> RouteUtil.unwrapTaskOption)
   )
   |> ignore

   app.MapPost(
      Path.Base,
      Func<ActorSystem, CreateAccountCommand, Task<IResult>>(fun system cmd ->
         processCommand
            system
            (AccountCommand.CreateAccount cmd)
            cmd.EntityId
            (cmd.toEvent ())
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.Deposit,
      Func<ActorSystem, DepositCashCommand, Task<IResult>>(fun sys cmd ->
         processCommand
            sys
            (AccountCommand.DepositCash cmd)
            cmd.EntityId
            (cmd.toEvent ())
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.Debit,
      Func<ActorSystem, DebitCommand, Task<IResult>>(fun sys cmd ->
         processCommand
            sys
            (AccountCommand.Debit cmd)
            cmd.EntityId
            (cmd.toEvent ())
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.DailyDebitLimit,
      Func<ActorSystem, LimitDailyDebitsCommand, Task<IResult>>(fun sys cmd ->
         processCommand
            sys
            (AccountCommand.LimitDailyDebits cmd)
            cmd.EntityId
            (cmd.toEvent ())
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.LockCard,
      Func<ActorSystem, LockCardCommand, Task<IResult>>(fun sys cmd ->
         processCommand
            sys
            (AccountCommand.LockCard cmd)
            cmd.EntityId
            (cmd.toEvent ())
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.UnlockCard,
      Func<ActorSystem, UnlockCardCommand, Task<IResult>>(fun sys cmd ->
         processCommand
            sys
            (AccountCommand.UnlockCard cmd)
            cmd.EntityId
            (cmd.toEvent ())
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      Path.CloseAccount,
      Func<ActorSystem, CloseAccountCommand, Task<IResult>>(fun sys cmd ->
         processCommand
            sys
            (AccountCommand.CloseAccount cmd)
            cmd.EntityId
            (cmd.toEvent ())
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapGet(
      Path.CircuitBreaker,
      Func<ActorSystem, Task<IResult>>(fun sys ->
         let ref = CircuitBreakerActor.get sys

         let (lookup: CircuitBreakerActorState Task) =
            ref <? CircuitBreakerMessage.Lookup |> Async.toTask

         lookup |> RouteUtil.unwrapTask)
   )
   |> ignore
