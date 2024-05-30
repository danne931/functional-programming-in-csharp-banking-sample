module Bank.Transaction.Api

open System
open FsToolkit.ErrorHandling

open Lib.SharedTypes
open Lib.TransactionQuery
open Lib.Postgres
open Bank.Account.Domain
open AncillaryTransactionInfoSqlMapper
open TransactionSqlMapper

module Fields = TransactionFields
module Writer = TransactionSqlWriter
module Reader = TransactionSqlReader

let transactionQuery (query: TransactionQuery) =
   let table = TransactionSqlMapper.table
   let txnLimit = 10

   let agg =
      [
         "accountId", Writer.accountId query.AccountId
         "offset", Sql.int <| Math.Max(query.Page - 1, 0) * txnLimit
      ],
      $"{Fields.accountId} = @accountId",
      false

   let agg =
      Option.fold
         (fun (queryParams, where, joinAncillary) (startDate, endDate) ->
            [
               "start", Writer.timestamp startDate
               "end", Writer.timestamp endDate
            ]
            @ queryParams,
            $"{where} AND {Fields.timestamp} >= @start 
                      AND {Fields.timestamp} <= @end",
            joinAncillary)
         agg
         query.DateRange

   let agg =
      match query.Diagnostic, query.MoneyFlow with
      | true, Some direction ->
         let queryParams, where, joinAncillary = agg

         let queryParams =
            [
               "direction", Writer.moneyFlow direction
               "diagnostic", Writer.moneyFlow MoneyFlow.None
            ]
            @ queryParams

         let where =
            where
            + $"""
            AND 
            (
               {Fields.moneyFlow} = @direction::{TransactionTypeCast.moneyFlow}
               OR {Fields.moneyFlow} = @diagnostic::{TransactionTypeCast.moneyFlow}
            )
            """

         queryParams, where, joinAncillary
      | false, None ->
         let queryParams, where, joinAncillary = agg

         ("diagnostic", Writer.moneyFlow MoneyFlow.None) :: queryParams,
         $"{where} AND {Fields.moneyFlow} <> @diagnostic::{TransactionTypeCast.moneyFlow}",
         joinAncillary
      | false, Some direction ->
         let queryParams, where, joinAncillary = agg

         ("direction", Writer.moneyFlow direction) :: queryParams,
         $"{where} AND {Fields.moneyFlow} = @direction::{TransactionTypeCast.moneyFlow}",
         joinAncillary
      | true, None -> agg

   let agg =
      Option.fold
         (fun (queryParams, where, joinAncillary) amountFilter ->
            let where, amountParams =
               match amountFilter with
               | AmountFilter.LessThanOrEqualTo max ->
                  $"{where} AND {Fields.amount} <= @max",
                  [ "max", Writer.amount (Some max) ]
               | AmountFilter.GreaterThanOrEqualTo min ->
                  $"{where} AND {Fields.amount} >= @min",
                  [ "min", Writer.amount (Some min) ]
               | AmountFilter.Between(min, max) ->
                  $"{where} AND {Fields.amount} >= @min AND {Fields.amount} <= @max",
                  [
                     "min", Writer.amount (Some min)
                     "max", Writer.amount (Some max)
                  ]

            amountParams @ queryParams, where, joinAncillary)
         agg
         query.Amount

   let atiTable = AncillaryTransactionInfoSqlMapper.table

   let agg =
      Option.fold
         (fun (queryParams, where, _) categoryFilter ->
            match categoryFilter with
            | CategoryFilter.CategoryIds catIds ->
               let catParam =
                  "categories",
                  AncillaryTransactionSqlWriter.categoryIds (Some catIds)

               catParam :: queryParams,
               $"{where} AND {atiTable}.{AncillaryTransactionFields.categoryId} = ANY(@categories)",
               true
            | CategoryFilter.IsCategorized isCat ->
               let isCatClause = if isCat then "IS NOT NULL" else "IS NULL"

               queryParams,
               $"{where} AND {atiTable}.{AncillaryTransactionFields.categoryId} {isCatClause}",
               true)
         agg
         query.Category

   let queryParams, where, joinAncillaryTransactionTable = agg

   let from =
      if joinAncillaryTransactionTable then
         $"{table} LEFT JOIN {atiTable} using ({Fields.transactionId})"
      else
         table

   queryParams,
   $"""
   SELECT {Fields.event}
   FROM {from}
   WHERE {where}
   ORDER BY timestamp desc
   LIMIT {txnLimit}
   OFFSET @offset
   """

let getTransactions (query: TransactionQuery) =
   let queryParams, queryString = transactionQuery query

   pgQuery<AccountEvent> queryString (Some queryParams) (fun read ->
      read.text Fields.event |> Serialization.deserializeUnsafe<AccountEvent>)

module Fields = AncillaryTransactionFields
module Writer = AncillaryTransactionSqlWriter
module Reader = AncillaryTransactionSqlReader
let private table = AncillaryTransactionInfoSqlMapper.table

let upsertTransactionCategory (transactionId: EventId) (categoryId: int) = taskResult {
   let query =
      $"""
      INSERT INTO {table}
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

let deleteTransactionCategory (transactionId: EventId) =
   let query =
      $"""
      UPDATE {table}
      SET {Fields.categoryId} = null
      WHERE {Fields.transactionId} = @txnId
      """

   pgPersist query [ "txnId", Writer.transactionId transactionId ]

let upsertTransactionNote (transactionId: EventId) (note: string) = taskResult {
   let query =
      $"""
      INSERT INTO {table}
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

let getTransactionInfo (txnId: EventId) =
   let query =
      $"""
      SELECT
         {TransactionSqlMapper.table}.{Fields.transactionId},
         {TransactionSqlMapper.table}.{Fields.event},
         {table}.{Fields.categoryId},
         {table}.{Fields.note},
         category.name as category_name
      FROM {TransactionSqlMapper.table}
         LEFT JOIN {table} using({Fields.transactionId})
         LEFT JOIN category using({Fields.categoryId})
      WHERE {Fields.transactionId} = @transactionId
      """

   let rowReader (read: RowReader) = {
      Id = Reader.transactionId read
      Event = Reader.event read
      Category =
         Reader.categoryId read
         |> Option.map (fun catId -> {
            Id = catId
            Name = read.string "category_name"
         })
      Note = Reader.note read
   }

   pgQuerySingle<TransactionWithAncillaryInfo>
      query
      (Some [ "transactionId", Writer.transactionId txnId ])
      rowReader
