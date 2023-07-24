using LanguageExt;
using static LanguageExt.Prelude;
using Echo;
using static Echo.Process;
using static System.Console;

using Lib;
using Lib.Types;
using Lib.BankTypes;
using Bank.Account.Domain;
using Bank.Transfer.Domain;
using Bank.Transfer.Actors;

namespace Bank.Account.Actors;

public record AccountRegistry(
   AccountPersistence persistence,
   AccountBroadcast broadcast
) {
   public Task<Option<AccountState>> Lookup(Guid id) {
      var process = "@" + AccountActor.PID(id);

      if (ActorUtil.IsAlive(process)) {
         var account = ask<AccountState>(process, new LookupCmd(id));
         return TaskSucc(Some(account));
      }

      return
         from acct in persistence.loadAccount(id)
         from pid in TaskSucc(Some(AccountActor.Start(persistence, broadcast, acct)))
         select acct;
   }
}

public static class AccountActor {
   public static ProcessId Start(
      AccountPersistence persistence,
      AccountBroadcast broadcaster,
      AccountState initialState
   ) {
      var pid = spawn<AccountState, Command>(
         PID(initialState.EntityId),
         () => initialState,
         (AccountState account, Command cmd) => {
            if (cmd is StartChildrenCmd) {
               MaintenanceFeeActor.Start(
                  persistence.loadAccountEvents,
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
                  broadcaster.broadcastError(errs.Head.Message);
                  WriteLine($"AccountActor: validation fail {errs.Head.Message}");
                  return account;
               },
               Succ: tup => {
                  var evt = tup.Event;

                  try {
                     persistence.save(evt).Wait();
                     broadcaster.broadcast(tup);
                  } catch (Exception err) {
                     broadcaster.broadcastError(err.Message);
                     WriteLine(err);
                     throw;
                  }


                  if (evt is TransferPending) {
                     var childName = TransferRecipientActor.ActorName;

                     if (!ActorUtil.IsAlive(child(childName))) {
                        TransferRecipientActor.Start(persistence);
                     }

                     tellChild(childName, (TransferPending) evt);
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
