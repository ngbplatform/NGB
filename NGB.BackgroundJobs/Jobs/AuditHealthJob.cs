using Dapper;
using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Contracts;
using NGB.Persistence.UnitOfWork;
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
    IUnitOfWork uow,
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

        await uow.EnsureConnectionOpenAsync(cancellationToken);

        const string triggerCheckSql = """
                                       SELECT COUNT(*)
                                       FROM pg_trigger t
                                       JOIN pg_class c ON c.oid = t.tgrelid
                                       JOIN pg_namespace n ON n.oid = c.relnamespace
                                       WHERE n.nspname = 'public'
                                         AND c.relname = @TableName
                                         AND t.tgname = @TriggerName
                                         AND NOT t.tgisinternal;
                                       """;

        var eventsTrigger = await uow.Connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                triggerCheckSql,
                new { TableName = "platform_audit_events", TriggerName = "trg_platform_audit_events_append_only" },
                uow.Transaction,
                cancellationToken: cancellationToken));

        var changesTrigger = await uow.Connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                triggerCheckSql,
                new { TableName = "platform_audit_event_changes", TriggerName = "trg_platform_audit_event_changes_append_only" },
                uow.Transaction,
                cancellationToken: cancellationToken));

        const string orphanChangesSql = """
                                       SELECT COUNT(*)
                                       FROM platform_audit_event_changes c
                                       LEFT JOIN platform_audit_events e ON e.audit_event_id = c.audit_event_id
                                       WHERE e.audit_event_id IS NULL;
                                       """;

        var orphanChanges = await uow.Connection.ExecuteScalarAsync<long>(
            new CommandDefinition(orphanChangesSql, transaction: uow.Transaction, cancellationToken: cancellationToken));

        const string volumeSql = """
                                 SELECT
                                     COUNT(*)::bigint                     AS events_count,
                                     MIN(occurred_at_utc)                 AS min_occurred_at_utc,
                                     MAX(occurred_at_utc)                 AS max_occurred_at_utc
                                 FROM platform_audit_events;
                                 """;

        var volume = await uow.Connection.QuerySingleAsync<AuditVolumeRow>(
            new CommandDefinition(volumeSql, transaction: uow.Transaction, cancellationToken: cancellationToken));

        metrics.Set("audit.events_trigger_present", eventsTrigger > 0 ? 1 : 0);
        metrics.Set("audit.changes_trigger_present", changesTrigger > 0 ? 1 : 0);
        metrics.Set("audit.missing_triggers", (eventsTrigger > 0 ? 0 : 1) + (changesTrigger > 0 ? 0 : 1));
        metrics.Set("audit.orphan_changes", orphanChanges);
        metrics.Set("audit.events_count", volume.EventsCount);

        logger.LogInformation(
            "[{JobId}] Metrics: EventsCount={EventsCount}, MinOccurredAtUtc={MinOccurredAtUtc:O}, MaxOccurredAtUtc={MaxOccurredAtUtc:O}",
            JobId,
            volume.EventsCount,
            volume.MinOccurredAtUtc,
            volume.MaxOccurredAtUtc);

        if (eventsTrigger <= 0 || changesTrigger <= 0)
        {
            throw new NgbInvariantViolationException(
                $"Audit append-only triggers are missing. eventsTrigger={eventsTrigger}, changesTrigger={changesTrigger}.");
        }

        if (orphanChanges > 0)
        {
            throw new NgbInvariantViolationException(
                $"Audit health failed: found {orphanChanges} orphan change rows (platform_audit_event_changes without platform_audit_events)." );
        }

        metrics.Set("health_ok", 1);

        var finishedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation(
            "[{JobId}] OK. OrphanChanges={OrphanChanges}. DurationMs={DurationMs}",
            JobId,
            orphanChanges,
            (long)(finishedAt - startedAt).TotalMilliseconds);
    }

    private sealed class AuditVolumeRow
    {
        public long EventsCount { get; init; }
        public DateTime? MinOccurredAtUtc { get; init; }
        public DateTime? MaxOccurredAtUtc { get; init; }
    }
}
