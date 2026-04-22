using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Extra indexes to support stable cursor paging of audit events.
/// </summary>
public sealed class PlatformAuditPagingIndexesMigration : IDdlObject
{
    public string Name => "platform_audit_paging_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_occurred_at_id_desc
                                    ON platform_audit_events(occurred_at_utc DESC, audit_event_id DESC);

                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_entity_occurred_at_id_desc
                                    ON platform_audit_events(entity_kind, entity_id, occurred_at_utc DESC, audit_event_id DESC);


                                -- Stable paging for "actor" filter (same cursor shape as global paging).
                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_actor_occurred_at_id_desc
                                    ON platform_audit_events(actor_user_id, occurred_at_utc DESC, audit_event_id DESC)
                                    WHERE actor_user_id IS NOT NULL;

                                -- Stable paging for "action_code" filter.
                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_action_occurred_at_id_desc
                                    ON platform_audit_events(action_code, occurred_at_utc DESC, audit_event_id DESC);

                                -- Stable paging for correlation tracing.
                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_correlation_occurred_at_id_desc
                                    ON platform_audit_events(correlation_id, occurred_at_utc DESC, audit_event_id DESC)
                                    WHERE correlation_id IS NOT NULL;
                                """;
}
