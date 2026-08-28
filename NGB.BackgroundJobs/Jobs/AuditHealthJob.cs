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

        const string healthSql = """
                                 SELECT
                                     (
                                         SELECT COUNT(*)
                                         FROM pg_trigger t
                                         JOIN pg_class trigger_table ON trigger_table.oid = t.tgrelid
                                         JOIN pg_namespace trigger_namespace ON trigger_namespace.oid = trigger_table.relnamespace
                                         WHERE trigger_namespace.nspname = 'public'
                                           AND trigger_table.relname = 'platform_audit_events'
                                           AND t.tgname = 'trg_platform_audit_events_append_only'
                                           AND NOT t.tgisinternal
                                     ) AS "EventsTrigger",
                                     (
                                         SELECT COUNT(*)
                                         FROM pg_trigger t
                                         JOIN pg_class trigger_table ON trigger_table.oid = t.tgrelid
                                         JOIN pg_namespace trigger_namespace ON trigger_namespace.oid = trigger_table.relnamespace
                                         WHERE trigger_namespace.nspname = 'public'
                                           AND trigger_table.relname = 'platform_audit_event_changes'
                                           AND t.tgname = 'trg_platform_audit_event_changes_append_only'
                                           AND NOT t.tgisinternal
                                     ) AS "ChangesTrigger",
                                     CASE WHEN EXISTS (
                                         SELECT 1
                                         FROM platform_audit_event_changes change
                                         WHERE NOT EXISTS (
                                             SELECT 1
                                             FROM platform_audit_events event
                                             WHERE event.audit_event_id = change.audit_event_id
                                         )
                                         LIMIT 1
                                     ) THEN 1::bigint ELSE 0::bigint END AS "OrphanChanges",
                                     GREATEST(c.reltuples, 0)::bigint AS "EventsCount",
                                     (
                                         SELECT occurred_at_utc
                                         FROM platform_audit_events
                                         ORDER BY occurred_at_utc, audit_event_id
                                         LIMIT 1
                                     ) AS "MinOccurredAtUtc",
                                     (
                                         SELECT occurred_at_utc
                                         FROM platform_audit_events
                                         ORDER BY occurred_at_utc DESC, audit_event_id DESC
                                         LIMIT 1
                                     ) AS "MaxOccurredAtUtc"
                                 FROM pg_class c
                                 JOIN pg_namespace n ON n.oid = c.relnamespace
                                 WHERE n.nspname = 'public'
                                   AND c.relname = 'platform_audit_events';
                                 """;

        var health = await uow.Connection.QuerySingleAsync<AuditHealthRow>(
            new CommandDefinition(healthSql, transaction: uow.Transaction, cancellationToken: cancellationToken));

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

    private sealed class AuditHealthRow
    {
        public long EventsTrigger { get; init; }
        public long ChangesTrigger { get; init; }
        public long OrphanChanges { get; init; }
        public long EventsCount { get; init; }
        public DateTime? MinOccurredAtUtc { get; init; }
        public DateTime? MaxOccurredAtUtc { get; init; }
    }
}
