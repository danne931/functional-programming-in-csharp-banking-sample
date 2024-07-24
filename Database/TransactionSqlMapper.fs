module TransactionSqlMapper

open System

open Bank.Account.Domain
open Lib.SharedTypes
open AccountSqlMapper
open OrganizationSqlMapper
open CardSqlMapper

let table = "transaction"

module TransactionTypeCast =
   let moneyFlow = "money_flow"

module TransactionFields =
   let transactionId = "transaction_id"
   let accountId = AccountFields.accountId
   let orgId = OrgFields.orgId
   let correlationId = "correlation_id"
   let cardId = CardFields.cardId
   let name = "name"
   let amount = "amount"
   let moneyFlow = "money_flow"
   let timestamp = "timestamp"
   let event = "event"

module TransactionSqlReader =
   let transactionId (read: RowReader) =
      TransactionFields.transactionId |> read.uuid |> EventId

   let accountId = AccountSqlReader.accountId

   let orgId = OrgSqlReader.orgId

   let correlationId (read: RowReader) =
      TransactionFields.correlationId |> read.uuid |> CorrelationId

   let cardId (read: RowReader) : CardId option =
      read.uuidOrNone TransactionFields.cardId |> Option.map CardId

   let name (read: RowReader) = read.text TransactionFields.name
   let amount (read: RowReader) = read.decimal TransactionFields.amount

   let moneyFlow (read: RowReader) =
      read.stringOrNone TransactionFields.moneyFlow
      |> Option.map MoneyFlow.fromString

   let timestamp (read: RowReader) =
      read.dateTime TransactionFields.timestamp

   let event (read: RowReader) =
      read.text TransactionFields.event
      |> Serialization.deserializeUnsafe<AccountEvent>

module TransactionSqlWriter =
   let transactionId (evtId: EventId) =
      let (EventId id) = evtId
      Sql.uuid id

   let correlationId (corrId: CorrelationId) =
      let (CorrelationId id) = corrId
      Sql.uuid id

   let cardId (cardId: CardId option) =
      let uuidOpt =
         cardId
         |> Option.map (fun cardId ->
            let (CardId id) = cardId
            id)

      Sql.uuidOrNone uuidOpt

   let cardIds (ids: CardId list) =
      ids |> List.map CardId.get |> Array.ofList |> Sql.uuidArray

   let accountId = AccountSqlWriter.accountId
   let orgId = OrgSqlWriter.orgId
   let name = Sql.text
   let amount = Sql.moneyOrNone

   let moneyFlow (direction: MoneyFlow option) =
      direction |> Option.map (string >> _.ToLower()) |> Sql.stringOrNone

   let timestamp (date: DateTime) = Sql.timestamptz date

   let event (event: AccountEvent) =
      Sql.jsonb <| Serialization.serialize event
