module Bank.Transaction.Routes

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder

open Bank.Account.Domain
open Bank.Transaction.Api
open RoutePaths
open Lib.NetworkQuery
open Lib.SharedTypes
open Bank.UserSession.Middleware

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
      string,
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
           ([<FromQuery>] cardIds: string)
           ([<FromQuery>] initiatedByIds: string)
           ([<FromQuery>] amountMin: Nullable<decimal>)
           ([<FromQuery>] amountMax: Nullable<decimal>)
           ([<FromQuery>] date: string) ->
         let moneyFlowOpt = MoneyFlow.fromString moneyFlow
         let dateOpt = dateRangeFromQueryString date
         let categories = CategoryFilter.categoryFromQueryString categoryIds

         let categoryOpt =
            if categories.IsSome then
               categories
            else if isCategorized.HasValue then
               Some(CategoryFilter.IsCategorized isCategorized.Value)
            else
               None

         func {
            AccountId = AccountId id
            Diagnostic = diagnostic
            Page = page
            MoneyFlow = moneyFlowOpt
            Category = categoryOpt
            Amount = AmountFilter.fromQuery amountMin amountMax
            DateRange = dateOpt
            CardIds = TransactionQuery.cardIdsFromQueryString cardIds
            InitiatedByIds =
               TransactionQuery.initiatedByIdsFromQueryString initiatedByIds
         })

let startTransactionRoutes (app: WebApplication) =
   app
      .MapGet(
         TransactionPath.AccountTransactions,
         withQueryParams<Task<IResult>> (
            getTransactions >> RouteUtil.unwrapTaskResultOption
         )
      )
      .RBAC(Permissions.GetTransactions)
   |> ignore

   app
      .MapGet(
         TransactionPath.Categories,
         Func<Task<IResult>>(getCategories >> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.GetCategories)
   |> ignore

   app
      .MapGet(
         TransactionPath.TransactionInfo,
         Func<Guid, Task<IResult>>(fun txnId ->
            getTransactionInfo (EventId txnId)
            |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.GetTransactionInfo)
   |> ignore

   app
      .MapGet(
         TransactionPath.TransactionConfirmation,
         Func<Guid, Task<IResult>>(fun correlationId ->
            getCorrelatedTransactionConfirmations (CorrelationId correlationId)
            |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.GetTransactionInfo)
   |> ignore

   app
      .MapPost(
         TransactionPath.Category,
         Func<Guid, int, Task<IResult>>(fun txnId categoryId ->
            upsertTransactionCategory (EventId txnId) categoryId
            |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManageTransactionCategory)
   |> ignore

   app
      .MapDelete(
         TransactionPath.CategoryDelete,
         Func<Guid, Task<IResult>>(fun txnId ->
            deleteTransactionCategory (EventId txnId)
            |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManageTransactionCategory)
   |> ignore

   app
      .MapPost(
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
      .RBAC(Permissions.ManageTransactionNotes)
   |> ignore

   app
      .MapGet(
         TransactionPath.Merchants,
         Func<Guid, Task<IResult>>(fun orgId ->
            getMerchants (OrgId orgId) |> RouteUtil.unwrapTaskResultOption)
      )
      .RBAC(Permissions.GetMerchants)
   |> ignore

   app
      .MapPost(
         TransactionPath.Merchants,
         Func<Merchant, Task<IResult>>(fun merchant ->
            upsertMerchant merchant |> RouteUtil.unwrapTaskResult)
      )
      .RBAC(Permissions.ManageMerchants)
   |> ignore
