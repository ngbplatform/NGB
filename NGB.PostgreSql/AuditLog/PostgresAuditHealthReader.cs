using Dapper;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.AuditLog;

public sealed class PostgresAuditHealthReader(IUnitOfWork uow) : IAuditHealthReader
{
    public async Task<AuditHealthSnapshot> ReadAsync(CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
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

        return await uow.Connection.QuerySingleAsync<AuditHealthSnapshot>(
            new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct));
    }
}
