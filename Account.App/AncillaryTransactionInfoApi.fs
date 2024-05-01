module Bank.AncillaryTransactionInfo.Api

open System
open FsToolkit.ErrorHandling

open Lib.Postgres
open TransactionSqlMapper
open Bank.Account.Domain

let updateTransactionCategory (transactionId: Guid) (categoryId: int) = taskResult {
   let query =
      $"""
      UPDATE transaction
      SET {TransactionFields.categoryId} = @categoryId
      WHERE {TransactionFields.transactionId} = @transactionId
      """

   let! res =
      pgPersist query [
         "transactionId", Sql.uuid transactionId
         "categoryId", Sql.int categoryId
      ]

   return res
}

let updateTransactionNote (transactionId: Guid) (note: string) = taskResult {
   let query =
      $"""
      UPDATE transaction
      SET {TransactionFields.note} = @note
      WHERE {TransactionFields.transactionId} = @transactionId
      """

   printfn "note %A" note

   let! res =
      pgPersist query [
         "transactionId", TransactionSqlWriter.transactionId transactionId
         "note", TransactionSqlWriter.note note
      ]

   return res
}

let getCategories () =
   pgQuery<TransactionCategory>
      "SELECT category_id, name FROM category"
      None
      (fun read -> {
         Id = read.int "category_id"
         Name = read.string "name"
      })

let getTransactionInfo (txnId: Guid) = taskResult {
   let query =
      $"""
      SELECT
         transaction.{TransactionFields.categoryId},
         transaction.{TransactionFields.note},
         category.name as category_name
      FROM transaction
         LEFT JOIN category using({TransactionFields.categoryId})
      WHERE {TransactionFields.transactionId} = @transactionId
      """

   let rowReader (read: RowReader) = {
      Id = txnId
      Category =
         TransactionSqlReader.categoryId read
         |> Option.map (fun catId -> {
            Id = catId
            Name = read.string "category_name"
         })
      Note = TransactionSqlReader.note read
   }

   let! infoOpt =
      pgQuery<AncillaryTransactionInfo>
         query
         (Some [ "transactionId", Sql.uuid txnId ])
         rowReader

   return
      match infoOpt with
      | None ->
         Some {
            Id = txnId
            Category = None
            Note = None
         }
      | Some info -> Some info.Head
}
