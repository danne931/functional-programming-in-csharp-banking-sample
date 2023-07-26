using LanguageExt;
using LanguageExt.Common;

namespace Lib.Types;

public delegate Validation<Err, T> Validator<T>(T t);

public record Err(string Message, int Code = 100) : Expected(Message, Code);

public abstract record Command(Guid EntityId) {
   public DateTime Timestamp { get; init; } = DateTime.UtcNow;
};

public abstract record Event(
   Guid EntityId,
   DateTime Timestamp,
   string Name,
   float Version = 1.0F
);

public record MaintenanceFeeCriteria(
   bool QualifyingDepositFound = false,
   bool DailyBalanceThreshold = false
);
