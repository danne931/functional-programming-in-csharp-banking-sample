module Bank.BillingCycle.Api

open System
open Lib.SharedTypes
open FsToolkit.ErrorHandling

open Lib.Postgres
open BillingStatement
open BillingSqlMapper

let getBillingStatement () =
   pgQuery<BillingStatement> $"SELECT * FROM {BillingSqlMapper.table}" None
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
            $"""
            SELECT {BillingFields.transactions}
            FROM {BillingSqlMapper.table}
            WHERE {BillingFields.accountId} = @accountId
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
