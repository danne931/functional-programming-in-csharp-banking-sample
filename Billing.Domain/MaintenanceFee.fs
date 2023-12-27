module MaintenanceFee

type MaintenanceFeeCriteria = {
   QualifyingDepositFound: bool
   DailyBalanceThreshold: bool
}

let DailyBalanceThreshold = 1500m
let QualifyingDeposit = 250m
let RecurringDebitAmount = 5m

let private applyThresholdCheck
   (criteria: MaintenanceFeeCriteria)
   (balance: decimal)
   =
   {
      criteria with
         DailyBalanceThreshold = balance >= DailyBalanceThreshold
   }

let private applyDepositCheck
   (criteria: MaintenanceFeeCriteria)
   (depositAmount: decimal)
   =
   {
      criteria with
         QualifyingDepositFound = depositAmount >= QualifyingDeposit
   }

// The balance must meet the threshold every day of the billing cycle
// in order to skip the maintenance fee.
let fromDebit (criteria: MaintenanceFeeCriteria) (balance: decimal) =
   if not criteria.DailyBalanceThreshold then
      criteria
   else
      applyThresholdCheck criteria balance

// If a debit causes a dip below the balance threshold, check
// if the balance meets the threshold criteria after the debit reversal.
// Ex: Balance: 1500
//     TransferPending: -100 (Balance 1400 - below threshold)
//     TransferRejected: +100 (Balance 1500 - satisfies threshold)
let fromDebitReversal = applyThresholdCheck

// A single qualifying deposit may be used in place of the
// daily balance criteria.
let fromDeposit (criteria: MaintenanceFeeCriteria) (depositAmount: decimal) =
   if criteria.QualifyingDepositFound then
      criteria
   else
      applyDepositCheck criteria depositAmount

// TODO: Handle case where a deposit was corrected by the issuer.
//       Example: A $250 deposit was issued by mistake.  The issuer
//                requests a correction to $25.
// let depositReversal =

let reset (balance: decimal) = {
   QualifyingDepositFound = false
   DailyBalanceThreshold = balance >= DailyBalanceThreshold
}
