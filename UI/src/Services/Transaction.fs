[<RequireQualifiedAccess>]
module TransactionService

open Fable.SimpleHttp
open Feliz.Router

open Bank.Account.Domain
open UIDomain
open UIDomain.Account
open Lib.SharedTypes
open RoutePaths
open Lib.NetworkQuery

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
      CardIds = query.SelectedCards |> Option.map (List.map _.CardId)
      InitiatedByIds =
         query.SelectedInitiatedBy
         |> Option.map (List.map (_.Id >> InitiatedById))
   }

let transactionQueryParams (query: TransactionQuery) : (string * string) list =
   let queryParams =
      [ "diagnostic", string query.Diagnostic; "page", string query.Page ]
      @ AccountBrowserQuery.toQueryParams {
         Amount = query.Amount
         Category = query.Category
         MoneyFlow = query.MoneyFlow
         SelectedCards = None
         SelectedInitiatedBy = None
         Date = None
         Action = None
         Transaction = None
      }

   let queryParams =
      match query.CardIds with
      | None -> queryParams
      | Some cardIds -> ("cardIds", listToQueryString cardIds) :: queryParams

   let queryParams =
      match query.InitiatedByIds with
      | None -> queryParams
      | Some ids -> ("initiatedByIds", listToQueryString ids) :: queryParams

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

let getCategories () : Async<Result<Map<int, TransactionCategory>, Err>> = async {
   let! (code, responseText) = Http.get TransactionPath.Categories

   if code <> 200 then
      return Error <| Err.InvalidStatusCodeError(serviceName, code)
   else
      return
         responseText
         |> Serialization.deserialize<TransactionCategory list>
         |> Result.map (List.map (fun o -> o.Id, o) >> Map.ofList)
}

let getMerchants (orgId: OrgId) : Async<Result<Map<string, Merchant>, Err>> = async {
   let! (code, responseText) = Http.get <| TransactionPath.merchants orgId

   if code = 404 then
      return Ok Map.empty
   elif code <> 200 then
      return Error <| Err.InvalidStatusCodeError(serviceName, code)
   else
      return
         responseText
         |> Serialization.deserialize<Merchant list>
         |> Result.map (List.map (fun o -> o.Name, o) >> Map.ofList)
}

let updateMerchant (merchant: Merchant) : Async<Result<int, Err>> = async {
   let! res =
      Http.postJson
         (TransactionPath.merchants merchant.OrgId)
         (Serialization.serialize merchant)

   let code = res.statusCode

   if code <> 200 then
      return Error <| Err.InvalidStatusCodeError(serviceName, code)
   else
      return Serialization.deserialize<int> res.responseText
}

let getTransactionInfo
   (txnId: EventId)
   : Async<Result<TransactionWithAncillaryInfo option, Err>>
   =
   async {
      let! (code, responseText) =
         Http.get (TransactionPath.transactionInfo txnId)

      if code = 404 then
         return Ok None
      elif code <> 200 then
         return Error <| Err.InvalidStatusCodeError(serviceName, code)
      else
         return
            responseText
            |> Serialization.deserialize<TransactionWithAncillaryInfo>
            |> Result.map Some
   }

let getCorrelatedTransactionConfirmations
   (correlationId: CorrelationId)
   : Async<Result<AccountEventPersistedConfirmation list option, Err>>
   =
   async {
      let! (code, responseText) =
         Http.get (TransactionPath.transactionConfirmation correlationId)

      if code = 404 then
         return Ok None
      elif code <> 200 then
         return Error <| Err.InvalidStatusCodeError(serviceName, code)
      else
         return
            responseText
            |> Serialization.deserialize<AccountEventPersistedConfirmation list>
            |> Result.map Some
   }

let updateCategory
   (txnId: EventId)
   (categoryId: int)
   : Async<Result<int, Err>>
   =
   async {
      let! code, responseText =
         Http.post
            (TransactionPath.category txnId categoryId)
            (string categoryId)

      if code <> 200 then
         return Error <| Err.InvalidStatusCodeError(serviceName, code)
      else
         return Serialization.deserialize<int> responseText
   }

let deleteCategory (txnId: EventId) : Async<Result<int, Err>> = async {
   let! code, responseText = Http.delete (TransactionPath.categoryDelete txnId)

   if code <> 200 then
      return Error <| Err.InvalidStatusCodeError(serviceName, code)
   else
      return Serialization.deserialize<int> responseText
}

let updateNote (txnId: EventId) (note: string) : Async<Result<int, Err>> = async {
   let! code, responseText = Http.post (TransactionPath.note txnId) note

   if code <> 200 then
      return Error <| Err.InvalidStatusCodeError(serviceName, code)
   else
      return Serialization.deserialize<int> responseText
}
