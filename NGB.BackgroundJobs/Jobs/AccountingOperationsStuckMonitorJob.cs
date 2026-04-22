using Microsoft.Extensions.Logging;
using NGB.Accounting.PostingState.Readers;
using NGB.BackgroundJobs.Contracts;
using NGB.Persistence.Readers.PostingState;
using NGB.Tools.Extensions;

namespace NGB.BackgroundJobs.Jobs;

/// <summary>
/// Optional, frequent: detects accounting posting operations that are stuck (stale in-progress).
///
/// Bounded work:
/// - reads only the first page of stale rows (PageSize) within a time window.
/// - does not mutate state; the posting log takeover logic remains in the write path.
///
/// By default this job DOES NOT throw when stale rows are found; it logs structured warnings.
/// </summary>
public sealed class AccountingOperationsStuckMonitorJob(
    IPostingStateReader reader,
    ILogger<AccountingOperationsStuckMonitorJob> logger,
    IJobRunMetrics metrics,
    TimeProvider? timeProvider = null)
    : IPlatformBackgroundJob
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string JobId => "accounting.operations.stuck_monitor";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation("[{JobId}] START at {StartedAtUtc:O}", JobId, startedAt);

        const int lookbackDays = 30;
        metrics.Set("lookback_days", lookbackDays);
        metrics.Set("warnings_logged", 0);

        const int pageSize = 25;
        var staleAfter = TimeSpan.FromMinutes(10);

        var request = new PostingStatePageRequest
        {
            PageSize = pageSize,
            Status = PostingStateStatus.StaleInProgress,
            StaleAfter = staleAfter,
            FromUtc = startedAt.AddDays(-lookbackDays),
            ToUtc = startedAt.AddDays(1)
        };

        var page = await reader.GetPageAsync(request, cancellationToken);
        var staleCount = page.Records.Count;

        metrics.Set("page_size", pageSize);
        metrics.Set("stale_after_minutes", (long)staleAfter.TotalMinutes);
        metrics.Set("stale_count", staleCount);
        metrics.Set("stale_rows_logged", staleCount);
        metrics.Set("warnings_logged", staleCount);
        metrics.Set("problem", staleCount == 0 ? 0 : 1);

        if (staleCount == 0)
        {
            var finishedAt = _timeProvider.GetUtcNowDateTime();
            logger.LogInformation(
                "[{JobId}] OK. StaleCount=0. DurationMs={DurationMs}",
                JobId,
                (long)(finishedAt - startedAt).TotalMilliseconds);
            return;
        }

        logger.LogWarning(
            "[{JobId}] FOUND stale posting operations: Count={Count}. Showing up to {PageSize}. StaleAfter={StaleAfter}.",
            JobId,
            staleCount,
            pageSize,
            staleAfter);

        foreach (var x in page.Records)
        {
            logger.LogWarning(
                "[{JobId}] Stale: DocumentId={DocumentId}, Operation={Operation}, StartedAtUtc={StartedAtUtc:O}, Age={Age}",
                JobId,
                x.DocumentId,
                x.Operation,
                x.StartedAtUtc,
                x.Age);
        }

        var finished = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation(
            "[{JobId}] DONE. StaleCount={Count}. DurationMs={DurationMs}",
            JobId,
            staleCount,
            (long)(finished - startedAt).TotalMilliseconds);
    }
}
