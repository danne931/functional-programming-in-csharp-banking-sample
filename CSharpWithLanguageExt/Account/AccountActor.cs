using LanguageExt;
using static LanguageExt.Prelude;
using Echo;
using static Echo.Process;
using static System.Console;

using Lib;
using Lib.Types;
using Bank.Account.Domain;

namespace Bank.Account.Actors;

public record AccountRegistry(
   Func<Guid, Task<Option<Lst<object>>>> loadAccountEvents,
   Func<Guid, Task<Option<AccountState>>> loadAccount,
   Func<Event, Task<Unit>> saveAndPublish,
   Func<(Event, AccountState), Task> broadcast,
   Func<string, Task> broadcastError
) {
   public Task<Option<AccountState>> Lookup(Guid id) {
      var process = "@" + AccountActor.PID(id);

      if (ActorUtil.IsAlive(process)) {
         var account = ask<AccountState>(process, new LookupCmd(id));
         return TaskSucc(Some(account));
      }

      return
         from acct in loadAccount(id)
         from pid in TaskSucc(Some(AccountActor.Start(acct, this)))
         select acct;
   }
}

public static class AccountActor {
   public static ProcessId Start(
      AccountState initialState,
      AccountRegistry registry
   ) {
      var pid = spawn<AccountState, Command>(
         PID(initialState.EntityId),
         () => initialState,
         (AccountState account, Command cmd) => {
            if (cmd is StartChildrenCmd) {
               MaintenanceFeeActor.Start(
                  registry.loadAccountEvents,
                  //lookBackDate: () => DateTime.UtcNow.AddDays(-30),
                  //scheduledAt: () => TimeSpan.FromDays(30),
                  lookBackDate: () => DateTime.UtcNow.AddMinutes(-2),
                  scheduledAt: () => TimeSpan.FromMinutes(2),
                  cmd.EntityId
               );
               return account;
            }
            if (cmd is LookupCmd) {
               reply(account);
               return account;
            }

            var validation = account.StateTransition(cmd);

            return validation.Match(
               Fail: errs => {
                  registry.broadcastError(errs.Head.Message);
                  WriteLine($"AccountActor: validation fail {errs.Head.Message}");
                  return account;
               },
               Succ: tup => {
                  try {
                     registry.saveAndPublish(tup.Event).Wait();
                     registry.broadcast(tup);
                  } catch (Exception err) {
                     registry.broadcastError(err.Message);
                     WriteLine(err);
                     throw;
                  }
                  return tup.NewState;
               }
            );
         }
      );

      register(pid.Name, pid);
      tell(pid, new StartChildrenCmd(initialState.EntityId));
      return pid;
   }

   public static Unit SyncStateChange(Command cmd) {
      tell("@" + PID(cmd.EntityId), cmd);
      return unit;
   }

   public static Unit Delete(Guid id) {
      var pid = "@" + PID(id);
      kill(pid);
      WriteLine($"Killed process {pid}");
      return unit;
   }

   public static string PID(Guid id) => $"accounts_{id}";

   private record StartChildrenCmd(Guid id) : Command(id);
}

record LookupCmd(Guid Id) : Command(Id);
