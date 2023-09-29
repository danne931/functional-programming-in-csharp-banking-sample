using LanguageExt;
using static LanguageExt.Prelude;
using Lib.Types;

namespace Bank.Account.Domain;

public static class Validators {
   public static readonly Lst<string> Currencies = List(
      "USD",
      "EUR",
      "THB",
      "VND"
   );

   public static Validator<CreateAccountCmd> AccountInitValidation() =>
      cmd => {
         // TODO: handle varying currency codes
         if (cmd.Balance < 100)
            return Fail<Err, CreateAccountCmd>(Errors.InvalidStartBalance(cmd.Balance));
         if (!Currencies.Contains(cmd.Currency))
            return Fail<Err, CreateAccountCmd>(Errors.InvalidCurrency(cmd.Currency));

         return Success<Err, CreateAccountCmd>(cmd);
      };

   public static Validator<LimitDailyDebitsCmd> DailyDebitLimitValidation() =>
      cmd => cmd.DebitLimit < 0
         ? Fail<Err, LimitDailyDebitsCmd>(Errors.InvalidDailyDebitLimit)
         : Success<Err, LimitDailyDebitsCmd>(cmd);
}