using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Contracts;
using NGB.Persistence.AuditLog;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.BackgroundJobs.Jobs;

/// <summary>
/// Nightly: basic audit-log health checks (safe, read-only).
///
/// This job is intentionally lightweight and bounded:
/// - verifies critical append-only triggers are present
/// - checks for referential integrity anomalies (orphan changes)
/// - surfaces basic volume/freshness metrics for monitoring
/// </summary>
public sealed class AuditHealthJob(
    IAuditHealthReader healthReader,
    ILogger<AuditHealthJob> logger,
    IJobRunMetrics metrics,
    TimeProvider? timeProvider = null)
    : IPlatformBackgroundJob
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string JobId => "audit.health";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation("[{JobId}] START at {StartedAtUtc:O}", JobId, startedAt);

        metrics.Set("health_ok", 0);

        var health = await healthReader.ReadAsync(cancellationToken);

        metrics.Set("audit.events_trigger_present", health.EventsTrigger > 0 ? 1 : 0);
        metrics.Set("audit.changes_trigger_present", health.ChangesTrigger > 0 ? 1 : 0);
        metrics.Set("audit.missing_triggers", (health.EventsTrigger > 0 ? 0 : 1) + (health.ChangesTrigger > 0 ? 0 : 1));
        metrics.Set("audit.orphan_changes", health.OrphanChanges);
        metrics.Set("audit.events_count", health.EventsCount);

        logger.LogInformation(
            "[{JobId}] Metrics: EventsCount={EventsCount}, MinOccurredAtUtc={MinOccurredAtUtc:O}, MaxOccurredAtUtc={MaxOccurredAtUtc:O}",
            JobId,
            health.EventsCount,
            health.MinOccurredAtUtc,
            health.MaxOccurredAtUtc);

        if (health.EventsTrigger <= 0 || health.ChangesTrigger <= 0)
            throw new NgbInvariantViolationException($"Audit append-only triggers are missing. eventsTrigger={health.EventsTrigger}, changesTrigger={health.ChangesTrigger}.");

        if (health.OrphanChanges > 0)
            throw new NgbInvariantViolationException($"Audit health failed: found {health.OrphanChanges} orphan change rows (platform_audit_event_changes without platform_audit_events)." );

        metrics.Set("health_ok", 1);

        var finishedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation(
            "[{JobId}] OK. OrphanChanges={OrphanChanges}. DurationMs={DurationMs}",
            JobId,
            health.OrphanChanges,
            (long)(finishedAt - startedAt).TotalMilliseconds);
    }
}
