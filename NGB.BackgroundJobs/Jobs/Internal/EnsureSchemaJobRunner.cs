using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Contracts;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.BackgroundJobs.Jobs.Internal;

internal static class EnsureSchemaJobRunner
{
    public static async Task RunAsync(
        ILogger logger,
        IJobRunMetrics metrics,
        string jobId,
        string registerKindForMessage,
        TimeProvider timeProvider,
        Func<CancellationToken, Task<(int TotalCount, int OkCount)>> ensure,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNowDateTime();
        logger.LogInformation("[{JobId}] START at {StartedAtUtc:O}", jobId, startedAt);

        var (total, ok) = await ensure(cancellationToken);

        metrics.Set("registers_total", total);
        metrics.Set("registers_ok", ok);

        var bad = total - ok;
        metrics.Set("registers_failed", bad);
        metrics.Set("has_failures", bad > 0 ? 1 : 0);

        if (bad > 0)
        {
            throw new NgbInvariantViolationException(
                $"{registerKindForMessage} schema is unhealthy after ensure. ok={ok}, total={total}.");
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
