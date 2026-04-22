using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Contracts;
using NGB.Persistence.Checkers;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.BackgroundJobs.Jobs;

/// <summary>
/// Nightly: checks that stored monthly accounting aggregates do not drift from the ledger.
///
/// Bounded work:
/// - checks current + previous month.
///
/// Fails the job if drift is detected (non-zero diff count).
/// </summary>
public sealed class AccountingAggregatesDriftCheckJob(
    IAccountingIntegrityDiagnostics diagnostics,
    ILogger<AccountingAggregatesDriftCheckJob> logger,
    IJobRunMetrics metrics,
    TimeProvider? timeProvider = null)
    : IPlatformBackgroundJob
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string JobId => "accounting.aggregates.drift_check";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation("[{JobId}] START at {StartedAtUtc:O}", JobId, startedAt);

        metrics.Set("periods_total", 2);
        metrics.Set("drift_detected", 0);

        var nowPeriod = JobPeriods.CurrentMonthStartUtc(startedAt);
        var prevPeriod = JobPeriods.AddMonths(nowPeriod, -1);

        var diffNow = await diagnostics.GetTurnoversVsRegisterDiffCountAsync(nowPeriod, cancellationToken);
        var diffPrev = await diagnostics.GetTurnoversVsRegisterDiffCountAsync(prevPeriod, cancellationToken);

        metrics.Set("periods_checked", 2);
        metrics.Set("diff_current", diffNow);
        metrics.Set("diff_previous", diffPrev);
        metrics.Set("diff_total", diffNow + diffPrev);

        metrics.Set("drift_detected", (diffNow > 0 || diffPrev > 0) ? 1 : 0);

        logger.LogInformation(
            "[{JobId}] DiffCounts: Current={DiffNow}, Previous={DiffPrev} (periods {CurrentPeriod}, {PrevPeriod}).",
            JobId,
            diffNow,
            diffPrev,
            nowPeriod,
            prevPeriod);

        if (diffNow > 0 || diffPrev > 0)
        {
            throw new NgbInvariantViolationException(
                $"Accounting aggregates drift detected. diff(current)={diffNow}, diff(previous)={diffPrev}."
            );
        }

        var finishedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation(
            "[{JobId}] OK. DurationMs={DurationMs}",
            JobId,
            (long)(finishedAt - startedAt).TotalMilliseconds);
    }
}
