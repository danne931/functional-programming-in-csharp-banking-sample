module Bank.AncillaryTransactionInfo.Routes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder

open Bank.AncillaryTransactionInfo.Api
open RoutePaths

let startAncillaryTransactionInfoRoutes (app: WebApplication) =
   app.MapGet(
      AncillaryTransactionInfoPath.Categories,
      Func<Task<IResult>>(getCategories >> RouteUtil.unwrapTaskResultOption)
   )
   |> ignore

   app.MapGet(
      AncillaryTransactionInfoPath.TransactionInfo,
      Func<Guid, Task<IResult>>(fun txnId ->
         getTransactionInfo txnId |> RouteUtil.unwrapTaskResultOption)
   )
   |> ignore

   app.MapPost(
      AncillaryTransactionInfoPath.Category,
      Func<Guid, int, Task<IResult>>(fun txnId categoryId ->
         upsertTransactionCategory txnId categoryId
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      AncillaryTransactionInfoPath.Note,
      Func<Guid, string, Task<IResult>>(fun txnId note ->
         upsertTransactionNote txnId note |> RouteUtil.unwrapTaskResult)
   )
   |> ignore
