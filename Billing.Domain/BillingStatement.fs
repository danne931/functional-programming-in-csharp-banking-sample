module BillingStatement

open System
open System.Threading.Tasks

open Lib.SharedTypes
open Bank.Account.Domain

type BillingTransaction = private BillingTransaction of AccountEvent

module BillingTransaction =
   let create (evt: AccountEvent) : BillingTransaction option =
      match evt with
      | CreatedAccount _
      | DepositedCash _
      | DebitedAccount _
      | MaintenanceFeeDebited _
      | TransferPending _
      | TransferRejected _
      | TransferDeposited _ -> Some(BillingTransaction evt)
      | _ -> None

   let value (BillingTransaction evt) = evt

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

let billingTransactions = List.choose BillingTransaction.create

#if !FABLE_COMPILER
open System.Text.Json

let billingStatement
   (account: AccountState)
   (lastPersistedEventSequenceNumber: Int64)
   : BillingStatement
   =
   {
      Transactions = billingTransactions account.Events
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
#endif
