module Bank.AncillaryTransactionInfo.Api

open System
open FsToolkit.ErrorHandling

open Lib.Postgres
open AncillaryTransactionInfoSqlMapper
open Bank.Account.Domain

module Fields = AncillaryTransactionFields
module Writer = AncillaryTransactionSqlWriter
module Reader = AncillaryTransactionSqlReader

let upsertTransactionCategory (transactionId: Guid) (categoryId: int) = taskResult {
   let query =
      $"""
      INSERT INTO ancillarytransactioninfo
         ({Fields.transactionId}, {Fields.categoryId})
      VALUES
         (@transactionId, @categoryId)
      ON CONFLICT ({Fields.transactionId})
      DO UPDATE SET {Fields.categoryId} = @categoryId
      """

   let! res =
      pgPersist query [
         "transactionId", Writer.transactionId transactionId
         "categoryId", Writer.categoryId (Some categoryId)
      ]

   return res
}

let upsertTransactionNote (transactionId: Guid) (note: string) = taskResult {
   let query =
      $"""
      INSERT INTO ancillarytransactioninfo
         ({Fields.transactionId}, {Fields.note}, {Fields.categoryId})
      VALUES
         (@transactionId, @note, @categoryId)
      ON CONFLICT ({Fields.transactionId})
      DO UPDATE SET {Fields.note} = @note
      """

   let! res =
      pgPersist query [
         "transactionId", Writer.transactionId transactionId
         "note", Writer.note note
         "categoryId", Writer.categoryId None
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
         ancillarytransactioninfo.{Fields.categoryId},
         ancillarytransactioninfo.{Fields.note},
         category.name as category_name
      FROM ancillarytransactioninfo
         LEFT JOIN category using({Fields.categoryId})
      WHERE {Fields.transactionId} = @transactionId
      """

   let rowReader (read: RowReader) = {
      Id = txnId
      Category =
         Reader.categoryId read
         |> Option.map (fun catId -> {
            Id = catId
            Name = read.string "category_name"
         })
      Note = Reader.note read
   }

   let! infoOpt =
      pgQuery<AncillaryTransactionInfo>
         query
         (Some [ "transactionId", Writer.transactionId txnId ])
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
