using NGB.Core.Locks;
using NGB.Persistence.Locks;

namespace NGB.Runtime.Locks;

public static class AdvisoryLockManagerExtensions
{
    public static async Task LockDocumentsDeterministicallyAsync(
        this IAdvisoryLockManager locks,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentNullException.ThrowIfNull(documentIds);

        var ordered = documentIds
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .OrderBy(static id => id)
            .ToArray();

        if (ordered.Length == 0)
            return;

        if (locks is IAdvisoryLockBatchManager batchLocks)
        {
            await batchLocks.LockDocumentsAsync(ordered, ct);
            return;
        }

        foreach (var documentId in ordered)
        {
            await locks.LockDocumentAsync(documentId, ct);
        }
    }

    public static async Task LockPeriodsDeterministicallyAsync(
        this IAdvisoryLockManager locks,
        IReadOnlyCollection<DateOnly> periods,
        AdvisoryLockPeriodScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentNullException.ThrowIfNull(periods);

        var ordered = periods
            .Select(static period => new DateOnly(period.Year, period.Month, 1))
            .Distinct()
            .OrderBy(static period => period)
            .ToArray();

        if (ordered.Length == 0)
            return;

        if (locks is IAdvisoryLockBatchManager batchLocks)
        {
            await batchLocks.LockPeriodsAsync(ordered, scope, ct);
            return;
        }

        foreach (var period in ordered)
        {
            if (scope == AdvisoryLockPeriodScope.Accounting)
                await locks.LockPeriodAsync(period, ct);
            else
                await locks.LockPeriodAsync(period, scope, ct);
        }
    }
}
