module Bank.Transaction.Routes

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder

open Bank.Account.Domain
open Bank.Transaction.Api
open RoutePaths
open Lib.TransactionQuery
open Lib.SharedTypes

/// Parse TransactionQuery-compliant query params from network request and
/// pass along to dotnet minimal api route handler.
let withQueryParams<'t> (func: TransactionQuery -> 't) =
   Func<
      Guid,
      bool,
      int,
      string,
      Nullable<bool>,
      string,
      Nullable<decimal>,
      Nullable<decimal>,
      string,
      't
    >
      (fun
           ([<FromRoute>] id: Guid)
           ([<FromQuery>] diagnostic: bool)
           ([<FromQuery>] page: int)
           ([<FromQuery>] moneyFlow: string)
           ([<FromQuery>] isCategorized: Nullable<bool>)
           ([<FromQuery>] categoryIds: string)
           ([<FromQuery>] amountMin: Nullable<decimal>)
           ([<FromQuery>] amountMax: Nullable<decimal>)
           ([<FromQuery>] date: string) ->
         let moneyFlowOpt =
            if not <| String.IsNullOrWhiteSpace moneyFlow then
               MoneyFlow.fromString moneyFlow
            else
               None

         let dateOpt =
            if not <| String.IsNullOrWhiteSpace date then
               TransactionQuery.dateRangeFromQueryString date
            else
               None

         let categoryOpt =
            if not <| String.IsNullOrWhiteSpace categoryIds then
               Some(TransactionQuery.categoryFromQueryString categoryIds)
            else if isCategorized.HasValue then
               Some(CategoryFilter.IsCategorized isCategorized.Value)
            else
               None

         let amountOpt =
            match amountMin.HasValue, amountMax.HasValue with
            | true, false ->
               Some(AmountFilter.GreaterThanOrEqualTo amountMin.Value)
            | false, true ->
               Some(AmountFilter.LessThanOrEqualTo amountMax.Value)
            | true, true ->
               Some(AmountFilter.Between(amountMin.Value, amountMax.Value))
            | _ -> None

         func {
            AccountId = AccountId id
            Diagnostic = diagnostic
            Page = page
            MoneyFlow = moneyFlowOpt
            Category = categoryOpt
            Amount = amountOpt
            DateRange = dateOpt
         })

let startTransactionRoutes (app: WebApplication) =
   app.MapGet(
      TransactionPath.AccountTransactions,
      withQueryParams<Task<IResult>> (
         getTransactions >> RouteUtil.unwrapTaskResultOption
      )
   )
   |> ignore

   app.MapGet(
      TransactionPath.Categories,
      Func<Task<IResult>>(getCategories >> RouteUtil.unwrapTaskResultOption)
   )
   |> ignore

   app.MapGet(
      TransactionPath.TransactionInfo,
      Func<Guid, Task<IResult>>(fun txnId ->
         getTransactionInfo (EventId txnId) |> RouteUtil.unwrapTaskResultOption)
   )
   |> ignore

   app.MapPost(
      TransactionPath.Category,
      Func<Guid, int, Task<IResult>>(fun txnId categoryId ->
         upsertTransactionCategory (EventId txnId) categoryId
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapDelete(
      TransactionPath.CategoryDelete,
      Func<Guid, Task<IResult>>(fun txnId ->
         deleteTransactionCategory (EventId txnId)
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      TransactionPath.Note,
      Func<Guid, Stream, Task<IResult>>(fun txnId body -> task {
         try
            use reader = new StreamReader(body)
            let! note = reader.ReadToEndAsync()

            return!
               upsertTransactionNote (EventId txnId) note
               |> RouteUtil.unwrapTaskResult
         with e ->
            return Results.Problem e.Message
      })
   )
   |> ignore

   app.MapGet(
      TransactionPath.Merchants,
      Func<Guid, Task<IResult>>(fun orgId ->
         getMerchants (OrgId orgId) |> RouteUtil.unwrapTaskResultOption)
   )
   |> ignore

   app.MapPost(
      TransactionPath.Merchants,
      Func<Merchant, Task<IResult>>(fun merchant ->
         upsertMerchant merchant |> RouteUtil.unwrapTaskResult)
   )
   |> ignore
