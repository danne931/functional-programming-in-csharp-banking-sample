module BillingStatement

open System

open Lib.SharedTypes
open Bank.Account.Domain

type BillingTransaction = private BillingTransaction of AccountEvent

type BillingPeriod = { Month: int; Year: int }

module BillingTransaction =
   let create
      (period: BillingPeriod)
      (evt: AccountEvent)
      : BillingTransaction option
      =
      let cutoffStart = DateTime(period.Year, period.Month, 1).ToUniversalTime()
      let cutoffEnd = cutoffStart.AddMonths(1)
      let _, env = AccountEnvelope.unwrap evt

      if env.Timestamp < cutoffStart || env.Timestamp >= cutoffEnd then
         None
      else
         match evt with
         | CreatedAccount _
         | DepositedCash _
         | DebitedAccount _
         | MaintenanceFeeDebited _
         | InternalTransferWithinOrgPending _
         | InternalTransferWithinOrgRejected _
         | InternalTransferWithinOrgDeposited _
         | InternalTransferBetweenOrgsPending _
         | InternalTransferBetweenOrgsRejected _
         | InternalTransferBetweenOrgsDeposited _
         | DomesticTransferPending _
         | DomesticTransferRejected _ -> Some(BillingTransaction evt)
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

let billingTransactions (period: BillingPeriod) (evts: AccountEvent list) =
   List.choose (BillingTransaction.create period) evts

#if !FABLE_COMPILER
open System.Text.Json

let billingStatement
   (state: AccountWithEvents)
   (period: BillingPeriod)
   (lastPersistedEventSequenceNumber: Int64)
   : BillingStatement
   =
   let account = state.Info
   let evts = state.Events

   {
      Transactions = billingTransactions period evts
      Month = period.Month
      Year = period.Year
      Balance = account.Balance
      Name = account.Name
      AccountId = account.AccountId
      OrgId = account.OrgId
      LastPersistedEventSequenceNumber = lastPersistedEventSequenceNumber
      AccountSnapshot =
         JsonSerializer.SerializeToUtf8Bytes(state, Serialization.jsonOptions)
   }
#endif
