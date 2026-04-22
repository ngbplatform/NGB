using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.BackgroundJobs.Infrastructure;

public sealed class PlatformHangfireJobRunner(
    IServiceScopeFactory scopeFactory,
    ILogger<PlatformHangfireJobRunner> logger,
    IPlatformJobNotifier notifier,
    JobStorage jobStorage,
    IOptions<PlatformHangfireOptions> options,
    TimeProvider? timeProvider = null)
{
    private readonly PlatformHangfireOptions _opts = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private static PlatformJobRunOutcome ParseOutcome(string outcome)
    {
        return outcome switch
        {
            "Succeeded" => PlatformJobRunOutcome.Succeeded,
            "Failed" => PlatformJobRunOutcome.Failed,
            "Cancelled" => PlatformJobRunOutcome.Cancelled,
            "SkippedOverlap" => PlatformJobRunOutcome.SkippedOverlap,
            "SkippedNoImplementation" => PlatformJobRunOutcome.SkippedNoImplementation,
            _ => PlatformJobRunOutcome.Failed
        };
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(string jobId, IJobCancellationToken cancellationToken)
    {
        var runId = Guid.CreateVersion7().ToString("N");

        using var scopeForContext = logger.BeginScope(new Dictionary<string, object>
        {
            ["JobId"] = jobId,
            ["RunId"] = runId
        });

        // Prevent overlap for the same JobId across all Hangfire servers.
        // If a previous run is still executing, this scheduled run is skipped.
        var lockResource = $"ngb:bgjob:{jobId}";

        using var connection = jobStorage.GetConnection();

        IDisposable jobLock;
        try
        {
            var timeoutSeconds = _opts.DistributedLockTimeoutSeconds;
            if (timeoutSeconds <= 0)
                timeoutSeconds = 1;

            jobLock = connection.AcquireDistributedLock(lockResource, TimeSpan.FromSeconds(timeoutSeconds));
        }
        catch (DistributedLockTimeoutException ex)
        {
            var timeoutSeconds = _opts.DistributedLockTimeoutSeconds;
            if (timeoutSeconds <= 0)
                timeoutSeconds = 1;

            // Keep skip-on-overlap semantics, but do not leak Hangfire-specific exception types to
            // the platform surface (notifier/telemetry). Treat it as a timeout.
            var timeoutEx = new NgbTimeoutException(
                operation: jobId,
                innerException: ex,
                additionalContext: new Dictionary<string, object?>
                {
                    ["lockResource"] = lockResource,
                    ["timeoutSeconds"] = timeoutSeconds,
                    ["reason"] = "hangfire.distributed_lock.timeout"
                });

            logger.LogInformation(
                "NGB.BackgroundJobs: skipped JobId={JobId} because the previous run is still executing (lock={LockResource}).",
                jobId,
                lockResource);

            var now = _timeProvider.GetUtcNowDateTime();

            logger.LogInformation(
                "NGB.BackgroundJobs: JobRunSummary JobId={JobId} RunId={RunId} Outcome={Outcome} DurationMs={DurationMs} Counters={Counters}",
                jobId,
                runId,
                "SkippedOverlap",
                0,
                new Dictionary<string, long> { ["skipped_overlap"] = 1 });

            await NotifyIfProblemAsync(
                logger,
                notifier,
                new PlatformJobRunResult(
                    jobId,
                    runId,
                    PlatformJobRunOutcome.SkippedOverlap,
                    now,
                    now,
                    0,
                    new Dictionary<string, long> { ["skipped_overlap"] = 1 },
                    Error: timeoutEx));
            return;
        }

        using (jobLock)
        {
            // Some scoped services (e.g. PostgresUnitOfWork) may implement IAsyncDisposable only.
            // Disposing a regular IServiceScope would call Dispose() and fail.
            await using var scope = scopeFactory.CreateAsyncScope();
            var metrics = scope.ServiceProvider.GetRequiredService<IJobRunMetrics>();
            var jobs = scope.ServiceProvider.GetServices<IPlatformBackgroundJob>();

            var job = jobs.FirstOrDefault(x => string.Equals(x.JobId, jobId, StringComparison.Ordinal));
            if (job is null)
            {
                logger.LogWarning("NGB.BackgroundJobs: no job implementation registered for JobId={JobId}.", jobId);

                metrics.Increment("skipped_no_implementation");
                var counters = metrics.Snapshot();
                logger.LogWarning(
                    "NGB.BackgroundJobs: JobRunSummary JobId={JobId} RunId={RunId} Outcome={Outcome} DurationMs={DurationMs} Counters={Counters}",
                    jobId,
                    runId,
                    "SkippedNoImplementation",
                    0,
                    counters);

                var now = _timeProvider.GetUtcNowDateTime();
                await NotifyIfProblemAsync(
                    logger,
                    notifier,
                    new PlatformJobRunResult(
                        jobId,
                        runId,
                        PlatformJobRunOutcome.SkippedNoImplementation,
                        now,
                        now,
                        0,
                        counters,
                        Error: null));
                return;
            }

            var startedUtc = _timeProvider.GetUtcNowDateTime();
            logger.LogInformation("NGB.BackgroundJobs: starting JobId={JobId} at {StartedUtc:o}.", jobId, startedUtc);

            var outcome = "Succeeded";
            Exception? error = null;
            var finishedUtc = startedUtc;

            try
            {
                await job.RunAsync(cancellationToken.ShutdownToken);
                finishedUtc = _timeProvider.GetUtcNowDateTime();
                logger.LogInformation(
                    "NGB.BackgroundJobs: finished JobId={JobId} at {FinishedUtc:o} (durationMs={DurationMs}).",
                    jobId,
                    finishedUtc,
                    (finishedUtc - startedUtc).TotalMilliseconds);
            }
            catch (OperationCanceledException)
            {
                outcome = "Cancelled";
                finishedUtc = _timeProvider.GetUtcNowDateTime();
                logger.LogWarning("NGB.BackgroundJobs: cancelled JobId={JobId}.", jobId);
                throw;
            }
            catch (Exception ex)
            {
                outcome = "Failed";
                error = NgbExceptionPolicy.Apply(
                    ex,
                    operation: jobId,
                    additionalContext: new Dictionary<string, object?>
                    {
                        ["runId"] = runId
                    });
                finishedUtc = _timeProvider.GetUtcNowDateTime();
                logger.LogError(error, "NGB.BackgroundJobs: failed JobId={JobId}.", jobId);
                throw error;
            }
            finally
            {
                var durationMs = (finishedUtc - startedUtc).TotalMilliseconds;
                if (durationMs < 0)
                    durationMs = 0;

                var counters = metrics.Snapshot();

                if (string.Equals(outcome, "Failed", StringComparison.Ordinal))
                {
                    logger.LogError(
                        error,
                        "NGB.BackgroundJobs: JobRunSummary JobId={JobId} RunId={RunId} Outcome={Outcome} StartedUtc={StartedUtc:o} FinishedUtc={FinishedUtc:o} DurationMs={DurationMs} Counters={Counters}",
                        jobId,
                        runId,
                        outcome,
                        startedUtc,
                        finishedUtc,
                        durationMs,
                        counters);
                }
                else if (string.Equals(outcome, "Cancelled", StringComparison.Ordinal))
                {
                    logger.LogWarning(
                        "NGB.BackgroundJobs: JobRunSummary JobId={JobId} RunId={RunId} Outcome={Outcome} StartedUtc={StartedUtc:o} FinishedUtc={FinishedUtc:o} DurationMs={DurationMs} Counters={Counters}",
                        jobId,
                        runId,
                        outcome,
                        startedUtc,
                        finishedUtc,
                        durationMs,
                        counters);
                }
                else
                {
                    logger.LogInformation(
                        "NGB.BackgroundJobs: JobRunSummary JobId={JobId} RunId={RunId} Outcome={Outcome} StartedUtc={StartedUtc:o} FinishedUtc={FinishedUtc:o} DurationMs={DurationMs} Counters={Counters}",
                        jobId,
                        runId,
                        outcome,
                        startedUtc,
                        finishedUtc,
                        durationMs,
                        counters);
                }

                // The platform does not send emails/Slack/etc. This hook allows a vertical application to do so.
                // We do NOT fail the job if the notifier fails; we only log the error.
                var result = new PlatformJobRunResult(
                    jobId,
                    runId,
                    ParseOutcome(outcome),
                    startedUtc,
                    finishedUtc,
                    (long)durationMs,
                    counters,
                    error);

                await NotifyIfProblemAsync(logger, notifier, result);
            }
        }
    }

    private static async Task NotifyIfProblemAsync(
        ILogger logger,
        IPlatformJobNotifier notifier,
        PlatformJobRunResult result)
    {
        if (!result.HasProblems)
            return;

        try
        {
            await notifier.NotifyAsync(result, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Best-effort: never fail a job because the notification channel is down.
            logger.LogError(
                ex,
                "NGB.BackgroundJobs: notification failed for JobId={JobId} RunId={RunId} Outcome={Outcome}.",
                result.JobId,
                result.RunId,
                result.Outcome);
        }
    }
}
