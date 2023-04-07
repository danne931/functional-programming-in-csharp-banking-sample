using LanguageExt;
using static LanguageExt.Prelude;
using Echo;
using static Echo.Process;
using static System.Console;

using Lib.Types;
using Bank.Account.Domain;

namespace Bank.Account.Actors;

public record AccountRegistry(
   Func<Guid, Task<Option<AccountState>>> loadAccount,
   Func<Event, Task<Unit>> saveAndPublish,
   Func<Guid, Lst<ProcessId>> startChildActors,
   Func<(Event, AccountState), Task> broadcast,
   Func<string, Task> broadcastError
) {
   public Task<Option<AccountState>> Lookup(Guid id) {
      var process = "@" + AccountActor.PID(id);

      var alive = Try(() => exists(process)).IfFail(err => {
         WriteLine($"Echo.Process.exists exception: {process} {err.Message}");
         return false;
      });

      if (alive) {
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
               var pids = registry.startChildActors(account.EntityId);
               WriteLine($"AccountActor: Started child actors {pids}");
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