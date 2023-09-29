using Lib.Types;

namespace Bank.Account.Domain;

public static class Errors {
   public static Err InsufficientBalance
      => new Err("Insufficient funds to fulfil the requested operation");

   public static Err AccountNotActive
      => new Err(nameof(AccountNotActive));

   public static Err AccountCardLocked
      => new Err(nameof(AccountCardLocked));

   public static Err UnknownAccountId(Guid id)
      => new Err($"{nameof(UnknownAccountId)}: {id.ToString()}");

   public static Err InvalidDepositAmount
      => new Err("Deposit amount must be greater than 0");

   public static Err InvalidCurrency(string currency)
      => new Err($"{nameof(InvalidCurrency)}: {currency}");

   public static Err InvalidStartBalance(decimal balance)
      => new Err($"Unable to open account with {balance} balance.");

   public static Err ExceededDailyDebitLimit(decimal limit)
      => new Err($"Daily debit limit exceeded {limit}");

   public static Err InvalidDailyDebitLimit
      => new Err($"Daily debit limit must be 0 or greater.");
}