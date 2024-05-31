module AncillaryTransactionInfoSqlMapper

open TransactionSqlMapper
open CategorySqlMapper

let table = "ancillarytransactioninfo"

module AncillaryTransactionFields =
   let transactionId = TransactionFields.transactionId
   let categoryId = CategoryFields.catId
   let note = "note"

module AncillaryTransactionSqlReader =
   let transactionId = TransactionSqlReader.transactionId

   let categoryId (read: RowReader) =
      read.intOrNone AncillaryTransactionFields.categoryId

   let note (read: RowReader) =
      read.textOrNone AncillaryTransactionFields.note

module AncillaryTransactionSqlWriter =
   let transactionId = TransactionSqlWriter.transactionId
   let categoryId = Sql.intOrNone

   let categoryIds (ids: int list option) =
      ids |> Option.map Array.ofList |> Sql.intArrayOrNone

   let note = Sql.text
