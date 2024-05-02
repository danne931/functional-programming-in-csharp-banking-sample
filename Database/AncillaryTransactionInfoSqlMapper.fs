module AncillaryTransactionInfoSqlMapper

open TransactionSqlMapper

module AncillaryTransactionFields =
   let transactionId = TransactionFields.transactionId
   let categoryId = "category_id"
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
   let note = Sql.text
