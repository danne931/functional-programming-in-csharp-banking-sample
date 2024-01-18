module Bank.BillingCycle.Api

open System

open Lib.Postgres
open BillingStatement

let getBillingStatement () =
   pgQuery<BillingStatement> "SELECT * FROM billingstatements" None
   <| fun (read: RowReader) -> {
      Transactions = read.fieldValue<BillingTransaction list> "transactions"
      Month = read.int "month"
      Year = read.int "year"
      Balance = read.decimal "balance"
      Name = read.text "name"
      AccountId = read.uuid "account_id"
      LastPersistedEventSequenceNumber =
         read.int64 "last_persisted_event_sequence_number"
      AccountSnapshot = read.bytea "account_snapshot"
   }

let private billingStatementSqlParams (bill: BillingStatement) = [
   "@transactions", Sql.jsonb <| Serialization.serialize bill.Transactions
   "@month", Sql.int bill.Month
   "@year", Sql.int bill.Year
   "@balance", Sql.money bill.Balance
   "@name", Sql.text bill.Name
   "@accountId", Sql.uuid bill.AccountId
   "@lastPersistedEventSequenceNumber",
   Sql.int64 bill.LastPersistedEventSequenceNumber
   "@accountSnapshot", Sql.bytea bill.AccountSnapshot
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
