using Lib.Types;

namespace Bank.Account.Domain;

public record CreateAccountCmd(
   Guid EntityId,
   string Currency,
   decimal Balance,
   string FirstName,
   string LastName
)
: Command(EntityId)
{
   public CreatedAccount ToEvent() =>
      new(EntityId, FirstName, LastName, Timestamp, Currency, Balance);
}

public record DepositCashCmd(
   Guid EntityId,
   DateTime Date,
   decimal Amount,
   string Origin = "ATM"
)
: Command(EntityId)
{
   public DepositedCash ToEvent() => new(
      DepositedAmount: Amount,
      EntityId: EntityId,
      Timestamp: Timestamp,
      Origin: Origin
   );
}

public record DebitCmd(
   Guid EntityId,
   DateTime Date,
   decimal Amount,
   string Origin,
   string? Reference = default
)
: Command(EntityId)
{
   public DebitedAccount ToEvent() => new(
      EntityId: EntityId,
      DebitedAmount: Amount,
      Date: Date,
      Timestamp: Timestamp,
      Origin: Origin,
      Reference: Reference
   );
}

public record LimitDailyDebitsCmd(
   Guid EntityId,
   decimal DebitLimit
)
: Command(EntityId)
{
   public DailyDebitLimitUpdated ToEvent() =>
      new(EntityId, DebitLimit, Timestamp);
}

public record LockCardCmd(
   Guid EntityId,
   string Reference
)
: Command(EntityId)
{
   public LockedCard ToEvent() => new(EntityId, Timestamp, Reference);
}

public record UnlockCardCmd(
   Guid EntityId,
   string Reference
)
: Command(EntityId)
{
   public UnlockedCard ToEvent() => new(EntityId, Timestamp, Reference);
}

public record MaintenanceFeeCmd(
   Guid EntityId,
   decimal Amount = 5
)
: Command(EntityId)
{
   public MaintenanceFeeDebited ToEvent() => new(EntityId, Timestamp, Amount);
}
