using LanguageExt;
using static LanguageExt.Prelude;
using static LanguageExt.List;
using EventStore.Client;

using Lib.Types;
using Lib.BankTypes;
using static Lib.Validators;
using ES = Lib.Persistence.EventStoreManager;
using AD = Bank.Account.Domain.Account;
using Bank.Account.Domain;
using static Bank.Account.Domain.Errors;
using Bank.Account.Actors;

namespace Bank.Account.API;

public static class AccountAPI {
   public static Task<Validation<Err, Guid>> Create(
      EventStoreClient client,
      AccountPersistence persistence,
      AccountBroadcast broadcaster,
      Validator<CreateAccountCmd> validate,
      CreateAccountCmd command
   ) {
      var save = async (CreateAccountCmd cmd) => {
         var evt = cmd.ToEvent();
         await ES.Save(
            client,
            AD.EventTypeMapping,
            AD.StreamName(evt.EntityId),
            evt,
            // Create event stream only if it doesn't already exist.
            StreamState.NoStream
         );
         return Pass<CreatedAccount>()(evt);
      };
      var registerAccount = (CreatedAccount evt) =>
         Optional(AccountActor.Start(persistence, broadcaster, AD.Create(evt)))
            .ToValidation(new Err("Account registration fail"))
            .AsTask();

      return
         from cmd in TaskSucc(validate(command))
         from evt in save(cmd)
         from _ in registerAccount(evt)
         select evt.EntityId;
   }

   public static Task<Option<Lst<object>>> GetAccountEvents(
      EventStoreClient client,
      Guid id
   )
   => ES.ReadStream(client, AD.StreamName(id), AD.EventTypeMapping);

   public static async Task<Option<AccountState>> GetAccount(
      Func<Guid, Task<Option<Lst<object>>>> getAccountEvents,
      Guid accountId
   ) {
      var res = await getAccountEvents(accountId);

      return res.Map(events =>
         fold(
            tail(events),
            AD.Create((CreatedAccount) head(events)),
            (state, evt) => state.Apply(evt)
         )
      );
   }

   /// <summary>
   /// Get all CreatedAccount events for UI demonstration purposes.
   /// Allows demonstration consumer to choose what account to process
   /// transactions on.
   /// </summary>
   public static Task<Option<Lst<object>>>
      GetAccountCreationEvents(EventStoreClient client) =>
         ES.ReadStream(
            client,
            "$et-CreatedAccount",
            AD.EventTypeMapping,
            resolveLinkTos: true
         );

   public static Task<bool> Exists(EventStoreClient es, Guid id) =>
      ES.Exists(es, AD.StreamName(id));

   public static Task<Validation<Err, Unit>> ProcessCommand<T>(
      T command,
      AccountRegistry registry,
      Validator<T> validate
   )
   where T : Command
   {
      var getAccountVal = (Command cmd) =>
         registry
            .Lookup(cmd.EntityId)
            .Map(opt => opt.ToValidation(UnknownAccountId(cmd.EntityId)));

      return
         from cmd in TaskSucc(validate(command))
         from acc in getAccountVal(cmd)
         select AccountActor.SyncStateChange(cmd);
   }

   public static Task<Unit> SoftDeleteEvents(
      EventStoreClient client,
      Guid accountId
   ) {
      AccountActor.Delete(accountId);
      return ES.SoftDelete(client, AD.StreamName(accountId));
   }

   public static async Task<Unit> Save(
      EventStoreClient esClient,
      Event evt
   ) {
      await ES.Save(
         esClient,
         AD.EventTypeMapping,
         AD.StreamName(evt.EntityId),
         evt
      );
      return unit;
   }
}
