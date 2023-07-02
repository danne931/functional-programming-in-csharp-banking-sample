using EventStore.Client;
using LanguageExt;

using Lib.BankTypes;
using static Lib.Validators;
using static Lib.Route.Response;
using AccountRegistry = Bank.Account.Actors.AccountRegistry;
using Bank.Account.API;
using Bank.Account.Domain;
using static Bank.Account.Domain.Validators;

namespace Bank.Account.Routes;

public static class AccountRoutes {
   public static class Path {
      public const string Base = "/accounts";
      public const string Diagnostic = "/diagnostic";
      public const string Deposit = $"{Base}/deposit";
      public const string Debit = $"{Base}/debit";
      public const string DailyDebitLimit = $"{Base}/daily-debit-limit";
      public const string LockCard = $"{Base}/lock";
      public const string UnlockCard = $"{Base}/unlock";
   }

   public static void Start(WebApplication app) {
      app.MapGet(Path.Base, GetAccounts);
      app.MapGet(Path.Base + "/{id}", GetAccount);
      app.MapGet(Path.Diagnostic + "/events/{id}", GetAccountEvents);
      app.MapDelete(Path.Diagnostic + "/events/{id}", SoftDeleteEvents);

      app.MapPost(Path.Base, CreateAccount);
      app.MapPost(Path.Deposit, Deposit);
      app.MapPost(Path.Debit, Debit);
      app.MapPost(Path.DailyDebitLimit, UpdateDailyDebitLimit);
      app.MapPost(Path.LockCard, LockCard);
      app.MapPost(Path.UnlockCard, UnlockCard);
   }

   // QUERY

   static Task<IResult> GetAccountEvents(
      Guid id,
      EventStoreClient es
   )
   => AccountAPI
      .GetAccountEvents(es, id)
      .Unwrap<Lst<object>>();

   static Task<IResult> SoftDeleteEvents(
      Guid id,
      EventStoreClient es
   )
   => AccountAPI
      .SoftDeleteEvents(es, id)
      .Unwrap<Unit>();

   static Task<IResult> GetAccounts(EventStoreClient es) =>
      AccountAPI
         .GetAccountCreationEvents(es)
         .Unwrap<Lst<object>>();

   static Task<IResult> GetAccount(
      Guid id,
      EventStoreClient es
   )
   => AccountAPI
      .GetAccount(id => AccountAPI.GetAccountEvents(es, id), id)
      .Unwrap<AccountState>(state => {
         // TEMPORARY fix LanguageExt.Map -> Dictionary
         // JSON parser doesn't seem to recognize Map type
         return new {
            TransferRecipients = state.TransferRecipients.ToDictionary(),
            EntityId = state.EntityId,
            FirstName = state.FirstName,
            LastName = state.LastName,
            Status = state.Status,
            Balance = state.Balance,
            DailyDebitLimit = state.DailyDebitLimit,
            DailyDebitAccrued = state.DailyDebitAccrued
         };
      });

   // COMMAND

   static Task<IResult> CreateAccount(
      CreateAccountCmd cmd,
      EventStoreClient es,
      AccountPersistence persistence,
      AccountBroadcast broadcaster
   )
   => AccountAPI
      .Create(
         es,
         persistence,
         broadcaster,
         AccountInitValidation(),
         cmd
      )
      .Unwrap<Guid>();

   static Task<IResult> Deposit(
      DepositCashCmd cmd,
      AccountRegistry accounts
   )
   => AccountAPI
      .ProcessCommand<DepositCashCmd>(cmd, accounts, Pass<DepositCashCmd>())
      .Unwrap<Unit>();

   static Task<IResult> Debit(
      DebitCmd cmd,
      AccountRegistry accounts
   )
   => AccountAPI
      .ProcessCommand<DebitCmd>(cmd, accounts, Pass<DebitCmd>())
      .Unwrap<Unit>();

   static Task<IResult> UpdateDailyDebitLimit(
      LimitDailyDebitsCmd cmd,
      AccountRegistry accounts
   )
   => AccountAPI
      .ProcessCommand<LimitDailyDebitsCmd>(cmd, accounts, DailyDebitLimitValidation())
      .Unwrap<Unit>();

   static Task<IResult> LockCard(
      LockCardCmd cmd,
      AccountRegistry accounts
   )
   => AccountAPI
      .ProcessCommand<LockCardCmd>(cmd, accounts, Pass<LockCardCmd>())
      .Unwrap<Unit>();

   static Task<IResult> UnlockCard(
      UnlockCardCmd cmd,
      AccountRegistry accounts
   )
   => AccountAPI
      .ProcessCommand<UnlockCardCmd>(cmd, accounts, Pass<UnlockCardCmd>())
      .Unwrap<Unit>();
}