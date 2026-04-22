using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Contracts;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.BackgroundJobs.Jobs.Internal;

internal static class RegisterEnsureSchemaJobRunner
{
    public static async Task RunAsync<TReport>(
        string jobId,
        string kindLabel,
        Func<CancellationToken, Task<TReport>> ensureSchemaAsync,
        Func<TReport, int> totalCount,
        Func<TReport, int> okCount,
        ILogger logger,
        IJobRunMetrics metrics,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var startedAt = timeProvider.GetUtcNowDateTime();
        logger.LogInformation("[{JobId}] START at {StartedAtUtc:O}", jobId, startedAt);

        var report = await ensureSchemaAsync(ct);

        var total = totalCount(report);
        var ok = okCount(report);

        metrics.Set("registers_total", total);
        metrics.Set("registers_ok", ok);

        var bad = total - ok;
        metrics.Set("registers_failed", bad);
        metrics.Set("has_failures", bad > 0 ? 1 : 0);
        
        if (bad > 0)
        {
            throw new NgbInvariantViolationException(
                $"{kindLabel} schema is unhealthy after ensure. ok={ok}, total={total}.");
        }

        var finishedAt = timeProvider.GetUtcNowDateTime();
        logger.LogInformation(
            "[{JobId}] OK. Total={Total}. Ok={Ok}. DurationMs={DurationMs}",
            jobId,
            total,
            ok,
            (long)(finishedAt - startedAt).TotalMilliseconds);
    }
}
