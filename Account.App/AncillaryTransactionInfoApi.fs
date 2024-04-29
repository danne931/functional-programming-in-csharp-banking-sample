module Bank.AncillaryTransactionInfo.Api

open System
open System.Threading.Tasks
open FSharp.Control
open FsToolkit.ErrorHandling

open Lib.SharedTypes
open Lib.Postgres
open Bank.Account.Domain

let upsertTransactionCategory (transactionId: Guid) (categoryId: int) = taskResult {
   let query =
      $"""
      INSERT into transactioncategory
         (transaction_id, category_id)
      VALUES
         (@transactionId, @categoryId)
      ON CONFLICT (transaction_id)
      DO UPDATE SET category_id = @categoryId
      """

   let! res =
      pgPersist query [
         "transactionId", Sql.uuid transactionId
         "categoryId", Sql.int categoryId
      ]

   return res
}

let upsertTransactionNote (transactionId: Guid) (note: string) = taskResult {
   let query =
      $"""
      INSERT into transactionnote
         (transaction_id, note)
      VALUES
         (@transactionId, @note)
      ON CONFLICT (transaction_id)
      DO UPDATE SET note = @note
      """

   let! res =
      pgPersist query [
         "transactionId", Sql.uuid transactionId
         "note", Sql.string note
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
      """
      SELECT
         transactioncategory.category_id as category_id,
         transactionnote.note as note,
         category.name as category_name
      FROM transactioncategory 
         FULL OUTER JOIN transactionnote using(transaction_id)
         LEFT JOIN category using(category_id)
      WHERE transaction_id = @transactionId
      """

   let rowReader (read: RowReader) = {
      Id = txnId
      Category =
         read.intOrNone "category_id"
         |> Option.map (fun catId -> {
            Id = catId
            Name = read.string "category_name"
         })
      Note = read.textOrNone "note"
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
