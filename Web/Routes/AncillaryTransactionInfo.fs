module Bank.AncillaryTransactionInfo.Routes

open System
open System.IO
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
         updateTransactionCategory txnId categoryId
         |> RouteUtil.unwrapTaskResult)
   )
   |> ignore

   app.MapPost(
      AncillaryTransactionInfoPath.Note,
      Func<Guid, Stream, Task<IResult>>(fun txnId body -> task {
         try
            use reader = new StreamReader(body)
            let! note = reader.ReadToEndAsync()

            return!
               updateTransactionNote txnId note |> RouteUtil.unwrapTaskResult
         with e ->
            return Results.Problem e.Message
      })
   )
   |> ignore
