module BillingStatement

open System

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
      | InternalTransferPending _
      | InternalTransferRejected _
      | DomesticTransferPending _
      | DomesticTransferRejected _
      | TransferDeposited _ -> Some(BillingTransaction evt)
      | _ -> None

   let value (BillingTransaction evt) = evt

type BillingStatement = {
   Transactions: BillingTransaction list
   Month: int
   Year: int
   Balance: decimal
   Name: string
   AccountId: AccountId
   OrgId: OrgId
   LastPersistedEventSequenceNumber: Int64
   AccountSnapshot: byte[]
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
   (state: AccountWithEvents)
   (lastPersistedEventSequenceNumber: Int64)
   : BillingStatement
   =
   let account = state.Info
   let evts = state.Events

   {
      Transactions = billingTransactions evts
      Month = DateTime.UtcNow.Month
      Year = DateTime.UtcNow.Year
      Balance = account.Balance
      Name = account.Name
      AccountId = account.AccountId
      OrgId = account.OrgId
      LastPersistedEventSequenceNumber = lastPersistedEventSequenceNumber
      AccountSnapshot =
         JsonSerializer.SerializeToUtf8Bytes(account, Serialization.jsonOptions)
   }
#endif
