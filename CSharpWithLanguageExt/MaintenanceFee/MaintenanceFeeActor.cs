using Echo;
using static Echo.Process;
using static System.Console;

using Lib.BankTypes;
using Bank.Account.Domain;

namespace Bank.Account.Actors;

public static class MaintenanceFeeActor {
   public const string ActorName = "monthly_maintenance_fee";

   public static ProcessId Start(
      Func<TimeSpan> scheduledAt,
      Guid accountId
   ) {
      var pid = spawn<Guid>(
         ActorName,
         evt => {
            WriteLine($"Monthly maintenance fee: {accountId}");
            var account = askParent<AccountState>(new LookupCmd(accountId));
            var criteria = account.MaintenanceFeeCriteria;

            if (criteria.QualifyingDepositFound || criteria.DailyBalanceThreshold) {
               WriteLine(
                  "Some criteria met for skipping the monthly maintenance fee: " +
                  $"Deposit ({criteria.QualifyingDepositFound}) / Balance ({criteria.DailyBalanceThreshold})");
               tellParent(new SkipMaintenanceFeeCmd(accountId, criteria));
            } else {
               tellParent(new MaintenanceFeeCmd(accountId));
            }

            tellSelf(evt, scheduledAt());
         }
      );

      tell(pid, accountId, scheduledAt());
      return pid;
   }
}
