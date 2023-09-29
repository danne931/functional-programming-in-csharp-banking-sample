using Lib.BankTypes;

namespace Bank.MaintenanceFee.Domain;

public static class MaintenanceFee {
   public const decimal DailyBalanceThreshold = 1500;
   public const decimal QualifyingDeposit = 250;

  // The balance must meet the threshold every day of the billing cycle
  // in order to skip the maintenance fee.
   public static AccountState FromDebit(AccountState account) {
      if (!account.MaintenanceFeeCriteria.DailyBalanceThreshold) return account;
      return ApplyThresholdCheck(account);
   }

   // If a debit causes a dip below the balance threshold, check
   // if the balance meets the threshold criteria after the debit reversal.
   // Ex: Balance: 1500
   //     TransferPending: -100 (Balance 1400 - below threshold)
   //     TransferRejected: +100 (Balance 1500 - satisfies threshold)
   public static AccountState FromDebitReversal(AccountState account)
     => ApplyThresholdCheck(account);

   // A single qualifying deposit may be used in place of the
   // daily balance criteria.
   public static AccountState FromDeposit(AccountState account, decimal DepositAmount) {
      if (account.MaintenanceFeeCriteria.QualifyingDepositFound) return account;
      return ApplyDepositCheck(account, DepositAmount);
   }

   // TODO: Handle case where a deposit was corrected by the issuer.
   //       Example: A $250 deposit was issued by mistake.  The issuer
   //                requests a correction to $25.
   // public static AccountState DepositReversal()

   public static AccountState Reset(AccountState account) =>
      account with {
         MaintenanceFeeCriteria = new (false, account.Balance >= DailyBalanceThreshold)
      };

   private static AccountState ApplyThresholdCheck(AccountState account) =>
      account with {
         MaintenanceFeeCriteria = account.MaintenanceFeeCriteria with {
            DailyBalanceThreshold = account.Balance >= DailyBalanceThreshold
         }
      };

   private static AccountState ApplyDepositCheck(
      AccountState account,
      decimal DepositAmount
   ) =>
      account with {
         MaintenanceFeeCriteria = account.MaintenanceFeeCriteria with {
            QualifyingDepositFound = DepositAmount >= QualifyingDeposit
         }
      };
}
