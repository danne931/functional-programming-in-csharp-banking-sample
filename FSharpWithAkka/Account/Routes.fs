module Bank.Account.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open EventStore.Client
open Akkling

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
      Func<EventStoreClient, Task<IResult>>(fun es ->
         getAccountCreationEvents es |> RouteUtil.unwrapTaskOption)
   )
   |> ignore

   app.MapGet(
      Path.Account,
      Func<EventStoreClient, Guid, Task<IResult>>(fun es id ->
         getAccount (getAccountEvents es) id |> RouteUtil.unwrapTaskOption)
   )
   |> ignore

   app.MapGet(
      Path.AccountEvents,
      Func<EventStoreClient, Guid, Task<IResult>>(fun es id ->
         getAccountEvents es id |> RouteUtil.unwrapTaskOption)
   )
   |> ignore

   app.MapPost(
      Path.Base,
      Func<EventStoreClient, IActorRef<AccountCoordinatorMessage>, CreateAccountCommand, Task<IResult>>
         (fun es coordinator command ->
            createAccount es coordinator (Validators.accountCreate ()) command
            |> RouteUtil.unwrapTaskValidation)
   )
   |> ignore

   app.MapDelete(
      Path.AccountEvents,
      Func<EventStoreClient, IActorRef<AccountCoordinatorMessage>, Guid, Task<IResult>>
         (fun es coordinator id ->
            softDeleteEvents es coordinator id |> RouteUtil.unwrapTask)
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
