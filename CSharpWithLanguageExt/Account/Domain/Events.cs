using Lib.Types;

namespace Bank.Account.Domain;

public record CreatedAccount(
   Guid EntityId,
   string FirstName,
   string LastName,
   DateTime Timestamp,
   string Currency,
   decimal Balance
)
: Event(EntityId, Timestamp, nameof(CreatedAccount));

public record LockedCard(
   Guid EntityId,
   DateTime Timestamp,
   string Reference
)
: Event(EntityId, Timestamp, nameof(LockedCard));

public record UnlockedCard(
   Guid EntityId,
   DateTime Timestamp,
   string Reference
)
: Event(EntityId, Timestamp, nameof(UnlockedCard));

public record DepositedCash(
   Guid EntityId,
   DateTime Timestamp,
   decimal DepositedAmount,
   string Origin
)
: Event(EntityId, Timestamp, nameof(DepositedCash));

public record DebitedAccount(
   Guid EntityId,
   DateTime Date,
   DateTime Timestamp,
   decimal DebitedAmount,
   string Origin,
   string Reference
) : Event(EntityId, Timestamp, nameof(DebitedAccount));

public record DailyDebitLimitUpdated(
   Guid EntityId,
   decimal DebitLimit,
   DateTime Timestamp
) : Event(EntityId, Timestamp, nameof(DailyDebitLimitUpdated));

public record MaintenanceFeeDebited(
   Guid EntityId,
   DateTime Timestamp,
   decimal DebitedAmount
)
: Event(EntityId, Timestamp, nameof(MaintenanceFeeDebited));

public record MaintenanceFeeSkipped(
   Guid EntityId,
   DateTime Timestamp,
   MaintenanceFeeCriteria Reason
)
: Event(EntityId, Timestamp, nameof(MaintenanceFeeSkipped));
