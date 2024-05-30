module BillingSqlMapper

open BillingStatement
open AccountSqlMapper
open OrganizationSqlMapper

let table = "billingstatement"

module BillingFields =
   let transactions = "transactions"
   let month = "month"
   let year = "year"
   let balance = "balance"
   let name = "name"
   let accountId = AccountFields.entityId
   let orgId = OrgFields.orgId
   let lastPersistedEventSequenceNumber = "last_persisted_event_sequence_number"
   let accountSnapshot = "account_snapshot"

module BillingSqlReader =
   let transactions (read: RowReader) =
      read.text BillingFields.transactions
      |> Serialization.deserializeUnsafe<BillingTransaction list>

   let month (read: RowReader) = read.int BillingFields.month
   let year (read: RowReader) = read.int BillingFields.year

   let balance (read: RowReader) = read.decimal BillingFields.balance

   let name (read: RowReader) = read.text BillingFields.name

   let accountId = AccountSqlReader.entityId

   let orgId = OrgSqlReader.orgId

   let lastPersistedEventSequenceNumber (read: RowReader) =
      read.int64 BillingFields.lastPersistedEventSequenceNumber

   let accountSnapshot (read: RowReader) =
      read.bytea BillingFields.accountSnapshot

module BillingSqlWriter =
   let transactions (txns: BillingTransaction list) =
      txns |> Serialization.serialize |> Sql.jsonb

   let month = Sql.int
   let year = Sql.int
   let balance = Sql.money
   let name = Sql.text
   let accountId = AccountSqlWriter.entityId
   let orgId = OrgSqlWriter.orgId
   let lastPersistedEventSequenceNumber = Sql.int64
   let accountSnapshot = Sql.bytea
