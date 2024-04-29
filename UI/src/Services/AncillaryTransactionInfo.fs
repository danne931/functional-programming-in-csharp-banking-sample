[<RequireQualifiedAccess>]
module AncillaryTransactionInfoService

open Fable.SimpleHttp
open System
open FsToolkit.ErrorHandling

open Bank.Account.Domain
open Lib.SharedTypes
open RoutePaths

let private serviceName = "AncillaryTransactionInfoService"

let getCategories () : Async<Result<TransactionCategory list, Err>> = async {
   let! (code, responseText) = Http.get AncillaryTransactionInfoPath.Categories

   if code <> 200 then
      return Error <| Err.InvalidStatusCodeError(serviceName, code)
   else
      return responseText |> Serialization.deserialize<TransactionCategory list>
}

let getAncillaryTransactionInfo
   (txnId: Guid)
   : Async<Result<AncillaryTransactionInfo, Err>>
   =
   async {
      let! (code, responseText) =
         Http.get (AncillaryTransactionInfoPath.transactionInfo txnId)

      if code <> 200 then
         return Error <| Err.InvalidStatusCodeError(serviceName, code)
      else
         return
            responseText |> Serialization.deserialize<AncillaryTransactionInfo>
   }

let updateCategory (txnId: Guid) (categoryId: int) : Async<Result<int, Err>> = asyncResult {
   let! code, responseText =
      Http.post
         (AncillaryTransactionInfoPath.category txnId categoryId)
         (string categoryId)

   if code <> 200 then
      return! Error <| Err.InvalidStatusCodeError(serviceName, code)
   else
      return! Serialization.deserialize<int> responseText
}

let updateNote (txnId: Guid) (note: string) : Async<Result<int, Err>> = asyncResult {
   let! code, responseText =
      Http.post (AncillaryTransactionInfoPath.note txnId) note

   if code <> 200 then
      return! Error <| Err.InvalidStatusCodeError(serviceName, code)
   else
      return! Serialization.deserialize<int> responseText
}
