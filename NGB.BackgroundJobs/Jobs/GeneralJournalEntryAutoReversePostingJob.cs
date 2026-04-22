using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Jobs;

/// <summary>
/// Frequent, bounded job that posts due auto-reversal General Journal Entries.
///
/// Design goals:
/// - idempotent: already-posted reversals are skipped by the runner
/// - bounded: limits successful posts per run
/// - concurrency-safe: runner delegates to document posting, which enforces per-document locks
/// </summary>
public sealed class GeneralJournalEntryAutoReversePostingJob(
    IServiceProvider services,
    ILogger<GeneralJournalEntryAutoReversePostingJob> logger,
    IJobRunMetrics metrics,
    TimeProvider? timeProvider = null)
    : IPlatformBackgroundJob
{
    internal const int MaxPostsPerRun = 100;
    internal const string PostedBy = "SYSTEM";

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string JobId => PlatformJobCatalog.AccountingGeneralJournalEntryAutoReversePostDue;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var runner = services.GetService<IGeneralJournalEntrySystemReversalRunner>();
        if (runner is null)
        {
            metrics.Set("runner_missing", 1);

            throw new NgbConfigurationViolationException(
                $"Background job '{JobId}' requires '{typeof(IGeneralJournalEntrySystemReversalRunner).FullName}' to be registered.",
                new Dictionary<string, object?>
                {
                    ["jobId"] = JobId,
                    ["requiredService"] = typeof(IGeneralJournalEntrySystemReversalRunner).FullName
                });
        }

        var startedAt = _timeProvider.GetUtcNowDateTime();
        var utcDate = DateOnly.FromDateTime(startedAt);

        logger.LogInformation("[{JobId}] START at {StartedAtUtc:O} for UtcDate={UtcDate}.", JobId, startedAt, utcDate);

        metrics.Set("utc_date_yyyymmdd", utcDate.Year * 10_000L + utcDate.Month * 100L + utcDate.Day);
        metrics.Set("max_posts_per_run", MaxPostsPerRun);
        metrics.Set("posted_count", 0);

        var posted = await runner.PostDueSystemReversalsAsync(
            utcDate,
            batchSize: MaxPostsPerRun,
            postedBy: PostedBy,
            ct: cancellationToken);

        metrics.Set("posted_count", posted);

        var finishedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation(
            "[{JobId}] OK. UtcDate={UtcDate}. PostedCount={PostedCount}. MaxPostsPerRun={MaxPostsPerRun}. DurationMs={DurationMs}",
            JobId,
            utcDate,
            posted,
            MaxPostsPerRun,
            (long)(finishedAt - startedAt).TotalMilliseconds);
    }
}
