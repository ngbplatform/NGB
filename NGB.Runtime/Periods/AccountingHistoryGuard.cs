using NGB.Persistence.Locks;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Periods;

internal static class AccountingHistoryGuard
{
    internal static readonly Guid ChainLockId = DeterministicGuid.Create("AccountingClosedPeriodChain");

    internal static Task LockForPostingAsync(IAdvisoryLockManager locks, DateOnly earliestPeriod, CancellationToken ct)
        => locks is IAccountingHistoryLockManager historyLocks
            ? historyLocks.LockAccountingHistoryExtensionAsync(earliestPeriod, ct)
            : locks.LockDocumentAsync(ChainLockId, ct);

    // The caller already holds ChainLockId. Adapters without the optional capability use
    // that same guard for all postings, preserving correctness with coarser concurrency.
    internal static Task LockForPeriodChangeAsync(IAdvisoryLockManager locks, CancellationToken ct)
        => locks is IAccountingHistoryLockManager historyLocks
            ? historyLocks.LockAccountingHistoryChangeAsync(ct)
            : Task.CompletedTask;
}
