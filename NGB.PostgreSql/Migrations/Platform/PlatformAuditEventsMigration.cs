using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Business AuditLog: immutable, append-only stream of audit events.
///
/// - Every event belongs to a business entity (entity_kind + entity_id).
/// - Each event may have 0..N field changes stored in platform_audit_event_changes.
/// - Mutations (UPDATE/DELETE) are forbidden by database guards (separate migration).
/// </summary>
public sealed class PlatformAuditEventsMigration : IDdlObject
{
    public string Name => "platform_audit_events";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_audit_events (
                                    audit_event_id UUID PRIMARY KEY,

                                    entity_kind SMALLINT NOT NULL,
                                    entity_id UUID NOT NULL,

                                    action_code TEXT NOT NULL,

                                    actor_user_id UUID NULL,

                                    occurred_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    correlation_id UUID NULL,
                                    metadata JSONB NULL,

                                    CONSTRAINT ck_platform_audit_events_action_code_nonempty CHECK (length(trim(action_code)) > 0),
                                    CONSTRAINT ck_platform_audit_events_action_code_maxlen CHECK (length(action_code) <= 200),

                                    CONSTRAINT fk_platform_audit_events_actor_user
                                        FOREIGN KEY (actor_user_id)
                                            REFERENCES platform_users(user_id)
                                            ON UPDATE RESTRICT
                                            ON DELETE RESTRICT
                                );
                                """;
}
