using Echo;
using static Echo.Process;
using LanguageExt;
using static System.Console;

using Bank.Account.Domain;
using MF = Bank.MaintenanceFee.Domain.MaintenanceFee;

namespace Bank.Account.Actors;

public static class MaintenanceFeeActor {
   public const string ActorName = "monthly_maintenance_fee";

   public static ProcessId Start(
      Func<Guid, Task<Option<Lst<object>>>> getAccountEvents,
      Func<DateTime> lookBackDate,
      Func<TimeSpan> scheduledAt,
      Guid accountId
   ) {
      var pid = spawn<Guid>(
         ActorName,
         async evt => {
            WriteLine($"Monthly maintenance fee: {accountId}");
            var eventsOpt = await getAccountEvents(accountId);

            eventsOpt.IfSome(events => {
               var res = MF.ComputeFeeCriteria(lookBackDate(), events);

               if (res.depositCriteria || res.balanceCriteria) {
                  WriteLine(
                     "Some criteria met for skipping the monthly maintenance fee: " +
                     $"Deposit ({res.depositCriteria}) / Balance ({res.balanceCriteria})");
                  return;
               }

               tellParent(new MaintenanceFeeCmd(accountId));
            });

            tellSelf(evt, scheduledAt());
         }
      );

      tell(pid, accountId, scheduledAt());
      return pid;
   }
}
