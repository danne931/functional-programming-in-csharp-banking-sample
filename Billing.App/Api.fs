module Bank.BillingCycle.Api

open System
open Lib.SharedTypes
open FsToolkit.ErrorHandling

open Lib.Postgres
open BillingStatement

module BillingSqlReader =
   let transactions (read: RowReader) =
      read.text "transactions"
      |> Serialization.deserializeUnsafe<BillingTransaction list>

   let month (read: RowReader) = read.int "month"
   let year (read: RowReader) = read.int "year"

   let balance (read: RowReader) = read.decimal "balance"

   let name (read: RowReader) = read.text "name"

   let accountId (read: RowReader) = read.uuid "account_id"

   let lastPersistedEventSequenceNumber (read: RowReader) =
      read.int64 "last_persisted_event_sequence_number"

   let accountSnapshot (read: RowReader) = read.bytea "account_snapshot"

module BillingSqlWriter =
   let transactions (txns: BillingTransaction list) =
      txns |> Serialization.serialize |> Sql.jsonb

   let month = Sql.int
   let year = Sql.int
   let balance = Sql.money
   let name = Sql.text
   let accountId = Sql.uuid
   let lastPersistedEventSequenceNumber = Sql.int64
   let accountSnapshot = Sql.bytea

let getBillingStatement () =
   pgQuery<BillingStatement> "SELECT * FROM billingstatements" None
   <| fun (read: RowReader) -> {
      Transactions = BillingSqlReader.transactions read
      Month = BillingSqlReader.month read
      Year = BillingSqlReader.year read
      Balance = BillingSqlReader.balance read
      Name = BillingSqlReader.name read
      AccountId = BillingSqlReader.accountId read
      LastPersistedEventSequenceNumber =
         BillingSqlReader.lastPersistedEventSequenceNumber read
      AccountSnapshot = BillingSqlReader.accountSnapshot read
   }

let private billingStatementSqlParams (bill: BillingStatement) = [
   "@transactions", BillingSqlWriter.transactions bill.Transactions
   "@month", BillingSqlWriter.month bill.Month
   "@year", BillingSqlWriter.year bill.Year
   "@balance", BillingSqlWriter.balance bill.Balance
   "@name", BillingSqlWriter.name bill.Name
   "@accountId", BillingSqlWriter.accountId bill.AccountId
   "@lastPersistedEventSequenceNumber",
   BillingSqlWriter.lastPersistedEventSequenceNumber
      bill.LastPersistedEventSequenceNumber
   "@accountSnapshot", BillingSqlWriter.accountSnapshot bill.AccountSnapshot
]

let private eventJournalSqlParams (bill: BillingStatement) = [
   "@persistenceId", Sql.text <| string bill.AccountId
   "@sequenceNumber", Sql.int64 bill.LastPersistedEventSequenceNumber
]

let private snapshotStoreSqlParams (bill: BillingStatement) = [
   "@persistenceId", Sql.text <| string bill.AccountId
   "@sequenceNumber", Sql.int64 bill.LastPersistedEventSequenceNumber
   "@created", Sql.int64 <| DateTime.UtcNow.ToFileTimeUtc()
   "@snapshot", Sql.bytea bill.AccountSnapshot
   "@serializerId", Sql.int 931
   "@manifest", Sql.string "AccountState"
]

// NOTE:
// Deleting akka_event_journal account events & taking a snapshot
// used to be implemented in the AccountActor application logic.
// These DB ops would occur for each account during a billing cycle,
// leading to Postgres connection pool errors.
// To fix this I have removed this application logic from the AccountActor,
// instead opting to carry out these DB ops in a transaction along with
// insertion of the billing statements.
let saveBillingStatements (statements: BillingStatement list) =
   let sqlParams =
      statements
      |> List.fold
         (fun acc bill -> {|
            Billing = billingStatementSqlParams bill :: acc.Billing
            EventJournal = eventJournalSqlParams bill :: acc.EventJournal
            SnapshotStore = snapshotStoreSqlParams bill :: acc.SnapshotStore
         |})
         {|
            Billing = []
            EventJournal = []
            SnapshotStore = []
         |}

   pgTransaction [
      """
      INSERT into billingstatements
         (transactions,
          month,
          year,
          balance,
          name,
          account_id,
          last_persisted_event_sequence_number,
          account_snapshot)
      VALUES
         (@transactions,
          @month,
          @year,
          @balance,
          @name,
          @accountId,
          @lastPersistedEventSequenceNumber,
          @accountSnapshot)
      """,
      sqlParams.Billing

      """
      UPDATE akka_event_journal
      SET
         deleted = true
      WHERE
         persistence_id = @persistenceId AND
         sequence_number <= @sequenceNumber
      """,
      sqlParams.EventJournal

      """
      INSERT into akka_snapshots
         (persistence_id,
          sequence_number,
          created,
          snapshot,
          serializer_id,
          manifest)
      VALUES
         (@persistenceId,
          @sequenceNumber,
          @created,
          @snapshot,
          @serializerId,
          @manifest)
      """,
      sqlParams.SnapshotStore
   ]

/// Get AccountEvents for a past billing cycle.
// Current account events are sourced from the account read model
// and are displayed in the Transaction Table component.
// If the user paginates for past transactions then the
// the account events are sourced instead from historical billing
// statement records as provided by this function.
let getPaginatedTransactions
   (accountId: Guid)
   (offset: int)
   : TaskResultOption<BillingTransaction list, Err>
   =
   taskResultOption {
      let! res =
         pgQuery<BillingTransaction list>
            """
            SELECT transactions
            FROM billingstatements
            WHERE account_id = @accountId
            ORDER BY created_at DESC
            LIMIT 1
            OFFSET @offset
            """
            (Some [
               "@accountId", BillingSqlWriter.accountId accountId
               "@offset", Sql.int offset
            ])
            BillingSqlReader.transactions

      return res.Head
   }
