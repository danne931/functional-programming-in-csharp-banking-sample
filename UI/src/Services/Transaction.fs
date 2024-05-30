[<RequireQualifiedAccess>]
module TransactionService

open Fable.SimpleHttp
open FsToolkit.ErrorHandling
open Feliz.Router

open Bank.Account.Domain
open Bank.Account.UIDomain
open Lib.SharedTypes
open RoutePaths
open Lib.TransactionQuery

let private serviceName = "TransactionService"

let transactionQueryFromAccountBrowserQuery
   (accountId: AccountId)
   (query: AccountBrowserQuery)
   : TransactionQuery
   =
   {
      AccountId = accountId
      Diagnostic = false
      Page = 1
      Category = query.Category
      MoneyFlow = query.MoneyFlow
      Amount = query.Amount
      DateRange = query.Date |> Option.map DateFilter.toDateRange
   }

let transactionQueryParams (query: TransactionQuery) : (string * string) list =
   let queryParams =
      [ "diagnostic", string query.Diagnostic; "page", string query.Page ]
      @ AccountBrowserQuery.toQueryParams {
         Amount = query.Amount
         Category = query.Category
         MoneyFlow = query.MoneyFlow
         Date = None
         Action = None
         Transaction = None
      }

   match query.DateRange with
   | None -> queryParams
   | Some(startDate, endDate) ->
      ("date", DateTime.rangeAsQueryString startDate endDate) :: queryParams

let getTransactions (query: TransactionQuery) : Async<TransactionsMaybe> = async {
   let basePath = TransactionPath.accountTransactions query.AccountId

   let queryParams = Router.encodeQueryString <| transactionQueryParams query

   let path = basePath + queryParams

   let! (code, responseText) = Http.get path

   if code = 404 then
      return Ok None
   elif code <> 200 then
      return Error <| Err.InvalidStatusCodeError("AccountService", code)
   else
      return
         responseText
         |> Serialization.deserialize<AccountEvent list>
         |> Result.map Some
}

let getCategories () : Async<Result<TransactionCategory list, Err>> = async {
   let! (code, responseText) = Http.get TransactionPath.Categories

   if code <> 200 then
      return Error <| Err.InvalidStatusCodeError(serviceName, code)
   else
      return responseText |> Serialization.deserialize<TransactionCategory list>
}

let getTransactionInfo
   (txnId: EventId)
   : Async<Result<TransactionWithAncillaryInfo, Err>>
   =
   async {
      let! (code, responseText) =
         Http.get (TransactionPath.transactionInfo txnId)

      if code <> 200 then
         return Error <| Err.InvalidStatusCodeError(serviceName, code)
      else
         return
            responseText
            |> Serialization.deserialize<TransactionWithAncillaryInfo>
   }

let updateCategory
   (txnId: EventId)
   (categoryId: int)
   : Async<Result<int, Err>>
   =
   asyncResult {
      let! code, responseText =
         Http.post
            (TransactionPath.category txnId categoryId)
            (string categoryId)

      if code <> 200 then
         return! Error <| Err.InvalidStatusCodeError(serviceName, code)
      else
         return! Serialization.deserialize<int> responseText
   }

let deleteCategory (txnId: EventId) : Async<Result<int, Err>> = asyncResult {
   let! code, responseText = Http.delete (TransactionPath.categoryDelete txnId)

   if code <> 200 then
      return! Error <| Err.InvalidStatusCodeError(serviceName, code)
   else
      return! Serialization.deserialize<int> responseText
}

let updateNote (txnId: EventId) (note: string) : Async<Result<int, Err>> = asyncResult {
   let! code, responseText = Http.post (TransactionPath.note txnId) note

   if code <> 200 then
      return! Error <| Err.InvalidStatusCodeError(serviceName, code)
   else
      return! Serialization.deserialize<int> responseText
}
