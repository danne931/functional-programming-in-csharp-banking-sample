using LanguageExt;
using static LanguageExt.List;

using Lib.Types;
using Lib.BankTypes;
using Bank.Account.Domain;
using AD = Bank.Account.Domain.Account;

namespace Bank.MaintenanceFee.Domain;

public static class MaintenanceFee {
   public const decimal DailyBalanceThreshold = 1500;
   public const decimal QualifyingDeposit = 250;

   // NOTE: This implementation would need to be refactored if utilizing event snapshotting.
   public static
      (bool depositCriteria, bool balanceCriteria, AccountState account)
      ComputeFeeCriteria(
         DateTime lookback,
         Lst<object> events
      ) =>
      foldUntil(
         events,
         (
            depositCriteria: false,
            balanceCriteria: true,
            account: AD.Create((CreatedAccount) head(events))
         ),
         (acc, evt) => {
            acc.account = acc.account.Apply(evt);

            if (((Event) evt).Timestamp < lookback) {
               acc.balanceCriteria = acc.account.Balance
                  >= DailyBalanceThreshold;
               return acc;
            }

            // Account balance must meet the balance threshold EVERY day
            // for the last month in order to skip the monthly fee.
            if (acc.account.Balance < DailyBalanceThreshold)
               acc.balanceCriteria = false;

            if (evt is DepositedCash &&
               ((DepositedCash) evt).DepositedAmount >= QualifyingDeposit
            ) acc.depositCriteria = true;

            return acc;
         },
         // If qualifying deposit found -> quit folding early.
         tup => tup.depositCriteria
      );
}
