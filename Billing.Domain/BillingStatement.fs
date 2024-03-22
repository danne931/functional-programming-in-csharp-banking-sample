module BillingStatement

open System
open System.Text.Json
open System.Threading.Tasks

open Lib.SharedTypes
open Bank.Account.Domain

type BillingTransaction = {
   EventId: Guid
   Name: string
   Amount: decimal
   Date: DateTime
   Info: string
}

type BillingStatement = {
   Transactions: BillingTransaction list
   Month: int
   Year: int
   Balance: decimal
   Name: string
   AccountId: Guid
   LastPersistedEventSequenceNumber: Int64
   AccountSnapshot: byte[]
}

type BillingPersistence = {
   saveBillingStatements: BillingStatement list -> Task<Result<int list, Err>>
}

type BillingCycleMessage =
   | BillingCycleFanout
   | BillingCycleFinished

type BillingStatementMessage =
   | RegisterBillingStatement of BillingStatement
   | GetFailedWrites

let eventToBillingTransaction (evt: AccountEvent) : BillingTransaction option =
   match evt with
   | CreatedAccount e ->
      Some {
         EventId = e.EntityId
         Name = e.EventName
         Amount = e.Data.Balance
         Date = e.Timestamp
         Info = ""
      }
   | DepositedCash e ->
      Some {
         EventId = e.EntityId
         Name = e.EventName
         Amount = e.Data.DepositedAmount
         Date = e.Timestamp
         Info = e.Data.Origin
      }
   | DebitedAccount e ->
      Some {
         EventId = e.EntityId
         Name = e.EventName
         Amount = -e.Data.DebitedAmount
         Date = e.Timestamp
         Info = e.Data.Origin
      }
   | MaintenanceFeeDebited e ->
      Some {
         EventId = e.EntityId
         Name = e.EventName
         Amount = -e.Data.DebitedAmount
         Date = e.Timestamp
         Info = ""
      }
   | TransferPending e ->
      Some {
         EventId = e.EntityId
         Name = e.EventName
         Amount = -e.Data.DebitedAmount
         Date = e.Timestamp
         Info =
            $"Recipient: {e.Data.Recipient.FirstName} {e.Data.Recipient.LastName}"
      }
   | TransferRejected e ->
      Some {
         EventId = e.EntityId
         Name = e.EventName
         Amount = e.Data.DebitedAmount
         Date = e.Timestamp
         Info =
            $"Recipient: {e.Data.Recipient.FirstName} {e.Data.Recipient.LastName} - Reason: {e.Data.Reason}"
      }
   | TransferDeposited e ->
      Some {
         EventId = e.EntityId
         Name = e.EventName
         Amount = e.Data.DepositedAmount
         Info = $"Received from: {e.Data.Origin}"
         Date = e.Timestamp
      }
   | _ -> None

let billingTransactions (account: AccountState) =
   account.Events |> List.choose eventToBillingTransaction

let billingStatement
   (account: AccountState)
   (lastPersistedEventSequenceNumber: Int64)
   : BillingStatement
   =
   {
      Transactions = billingTransactions account
      Month = DateTime.Today.Month
      Year = DateTime.Today.Year
      Balance = account.Balance
      Name = account.Name
      AccountId = account.EntityId
      LastPersistedEventSequenceNumber = lastPersistedEventSequenceNumber
      AccountSnapshot =
         JsonSerializer.SerializeToUtf8Bytes(
            { account with Events = [] },
            Serialization.jsonOptions
         )
   }
