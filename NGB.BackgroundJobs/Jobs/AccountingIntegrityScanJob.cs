using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Contracts;
using NGB.Persistence.Checkers;
using NGB.Tools.Extensions;

namespace NGB.BackgroundJobs.Jobs;

/// <summary>
/// Nightly: checks that the ledger is balanced for a small rolling window of periods.
///
/// Bounded work:
/// - scans current month + previous month (UTC month-start), which is usually enough to catch drift early.
///
/// The checker is read-only and must be safe to run concurrently with normal posting.
/// </summary>
public sealed class AccountingIntegrityScanJob(
    IAccountingIntegrityChecker checker,
    ILogger<AccountingIntegrityScanJob> logger,
    IJobRunMetrics metrics,
    TimeProvider? timeProvider = null)
    : IPlatformBackgroundJob
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string JobId => "accounting.integrity.scan";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation("[{JobId}] START at {StartedAtUtc:O}", JobId, startedAt);

        const int totalPeriods = 2;
        metrics.Set("periods_total", totalPeriods);
        metrics.Set("periods_scanned", 0);

        var nowPeriod = JobPeriods.CurrentMonthStartUtc(startedAt);
        var prevPeriod = JobPeriods.AddMonths(nowPeriod, -1);

        var scanned = 0;

        await checker.AssertPeriodIsBalancedAsync(nowPeriod, cancellationToken);
        scanned++;

        metrics.Set("periods_scanned", scanned);

        await checker.AssertPeriodIsBalancedAsync(prevPeriod, cancellationToken);
        scanned++;

        metrics.Set("periods_scanned", scanned);

        var finishedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation(
            "[{JobId}] OK. PeriodsScanned={Scanned}. Current={CurrentPeriod}. Previous={PrevPeriod}. DurationMs={DurationMs}",
            JobId,
            scanned,
            nowPeriod,
            prevPeriod,
            (long)(finishedAt - startedAt).TotalMilliseconds);
    }
}
