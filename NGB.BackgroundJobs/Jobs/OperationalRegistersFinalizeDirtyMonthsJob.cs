using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Contracts;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;

namespace NGB.BackgroundJobs.Jobs;

/// <summary>
/// Nightly: finalize a bounded number of dirty operational register months.
///
/// Bounded work:
/// - uses IOperationalRegisterFinalizationRunner's maxItems (default 50).
/// - each month is processed under a month-level advisory lock in OperationalRegister scope.
/// </summary>
public sealed class OperationalRegistersFinalizeDirtyMonthsJob(
    IOperationalRegisterAdminMaintenanceService maintenance,
    ILogger<OperationalRegistersFinalizeDirtyMonthsJob> logger,
    IJobRunMetrics metrics,
    TimeProvider? timeProvider = null)
    : IPlatformBackgroundJob
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string JobId => "opreg.finalization.run_dirty_months";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation("[{JobId}] START at {StartedAtUtc:O}", JobId, startedAt);

        // Default matches the admin endpoint and is intentionally bounded.
        const int maxItems = 50;
        metrics.Set("max_items", maxItems);
        metrics.Set("finalized_count", 0);

        var finalized = await maintenance.FinalizeDirtyAsync(maxItems, cancellationToken);
        metrics.Set("finalized_count", finalized);

        var finishedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation(
            "[{JobId}] OK. FinalizedCount={Finalized}. MaxItems={MaxItems}. DurationMs={DurationMs}",
            JobId,
            finalized,
            maxItems,
            (long)(finishedAt - startedAt).TotalMilliseconds);
    }
}
