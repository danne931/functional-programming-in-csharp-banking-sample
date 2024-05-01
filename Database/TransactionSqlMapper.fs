module TransactionSqlMapper

open Npgsql.FSharp
open System

open Bank.Account.Domain
open Lib.SharedTypes

module TransactionTypeCast =
   let moneyFlow = "money_flow"

module TransactionFields =
   let transactionId = "transaction_id"
   let accountId = "account_id"
   let correlationId = "correlation_id"
   let name = "name"
   let amount = "amount"
   let moneyFlow = "money_flow"
   let categoryId = "category_id"
   let note = "note"
   let timestamp = "timestamp"
   let event = "event"

module TransactionSqlReader =
   let transactionId (read: RowReader) =
      read.uuid TransactionFields.transactionId

   let accountId (read: RowReader) = read.uuid TransactionFields.accountId

   let correlationId (read: RowReader) =
      read.uuid TransactionFields.correlationId

   let name (read: RowReader) = read.text TransactionFields.name
   let amount (read: RowReader) = read.decimal TransactionFields.amount

   let moneyFlow (read: RowReader) =
      read.string TransactionFields.moneyFlow
      |> Serialization.deserializeUnsafe<MoneyFlow>

   let categoryId (read: RowReader) =
      read.intOrNone TransactionFields.categoryId

   let note (read: RowReader) = read.textOrNone TransactionFields.note

   let timestamp (read: RowReader) =
      read.dateTime TransactionFields.timestamp

   let event (read: RowReader) =
      read.text TransactionFields.event
      |> Serialization.deserializeUnsafe<AccountEvent>

module TransactionSqlWriter =
   let transactionId = Sql.uuid
   let accountId = Sql.uuid
   let correlationId = Sql.uuid
   let name = Sql.text
   let amount = Sql.moneyOrNone

   let moneyFlow (direction: MoneyFlow) =
      direction |> string |> _.ToLower() |> Sql.string

   let categoryId = Sql.intOrNone
   let note = Sql.text
   let timestamp (date: DateTime) = Sql.timestamptz date

   let event (event: AccountEvent) =
      Sql.jsonb <| Serialization.serialize event
