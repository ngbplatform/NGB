using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformAuditIndexesMigration : IDdlObject
{
    public string Name => "platform_audit_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_occurred_at
                                    ON platform_audit_events(occurred_at_utc);

                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_entity
                                    ON platform_audit_events(entity_kind, entity_id, occurred_at_utc DESC);

                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_action
                                    ON platform_audit_events(action_code, occurred_at_utc DESC);

                                CREATE INDEX IF NOT EXISTS ix_platform_audit_events_actor
                                    ON platform_audit_events(actor_user_id, occurred_at_utc DESC)
                                    WHERE actor_user_id IS NOT NULL;

                                CREATE INDEX IF NOT EXISTS ix_platform_audit_event_changes_event
                                    ON platform_audit_event_changes(audit_event_id, ordinal);

                                CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_audit_event_changes_event_ordinal
                                    ON platform_audit_event_changes(audit_event_id, ordinal);
                                """;
}

