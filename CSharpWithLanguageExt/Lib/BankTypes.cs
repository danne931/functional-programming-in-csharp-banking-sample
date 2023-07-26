using LanguageExt;

using Lib.Types;
using Bank.Transfer.Domain;

namespace Lib.BankTypes;

public sealed record AccountState(
   Guid EntityId,
   string FirstName,
   string LastName,
   string Currency,
   MaintenanceFeeCriteria MaintenanceFeeCriteria,
   AccountStatus Status = AccountStatus.Active,
   decimal Balance = 0,
   decimal AllowedOverdraft = 0,
   decimal DailyDebitLimit = -1,
   decimal DailyDebitAccrued = 0,
   DateTime LastDebitDate = default,
   Map<string, TransferRecipient> TransferRecipients = default
) {};

public enum AccountStatus {
   Active,
   ActiveWithLockedCard,
   Closed
}

public sealed record AccountPersistence(
   Func<Guid, Task<Option<Lst<object>>>> loadAccountEvents,
   Func<Guid, Task<Option<AccountState>>> loadAccount,
   Func<Guid, Task<bool>> exists,
   Func<Event, Task<Unit>> save
) {};

public sealed record AccountBroadcast(
   Func<(Event, AccountState), Task> broadcast,
   Func<string, Task> broadcastError
) {};
