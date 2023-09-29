module MaintenanceFee

open Lib.Types
open BankTypes

let DailyBalanceThreshold = 1500m
let QualifyingDeposit = 250m

let private applyThresholdCheck (account: AccountState) = {
   account with
      MaintenanceFeeCriteria = {
         account.MaintenanceFeeCriteria with
            DailyBalanceThreshold = account.Balance >= DailyBalanceThreshold
      }
}

let private applyDepositCheck (account: AccountState) (depositAmount: decimal) = {
   account with
      MaintenanceFeeCriteria = {
         account.MaintenanceFeeCriteria with
            QualifyingDepositFound = depositAmount >= QualifyingDeposit
      }
}

// The balance must meet the threshold every day of the billing cycle
// in order to skip the maintenance fee.
let fromDebit (account: AccountState) =
   if not account.MaintenanceFeeCriteria.DailyBalanceThreshold then
      account
   else
      applyThresholdCheck account

// If a debit causes a dip below the balance threshold, check
// if the balance meets the threshold criteria after the debit reversal.
// Ex: Balance: 1500
//     TransferPending: -100 (Balance 1400 - below threshold)
//     TransferRejected: +100 (Balance 1500 - satisfies threshold)
let fromDebitReversal = applyThresholdCheck

// A single qualifying deposit may be used in place of the
// daily balance criteria.
let fromDeposit (account: AccountState) (depositAmount: decimal) =
   if account.MaintenanceFeeCriteria.QualifyingDepositFound then
      account
   else
      applyDepositCheck account depositAmount

// TODO: Handle case where a deposit was corrected by the issuer.
//       Example: A $250 deposit was issued by mistake.  The issuer
//                requests a correction to $25.
// let depositReversal =

let reset (account: AccountState) = {
   account with
      MaintenanceFeeCriteria = {
         QualifyingDepositFound = false
         DailyBalanceThreshold = account.Balance >= DailyBalanceThreshold
      }
}
