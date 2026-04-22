using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Field-level changes for business audit events.
///
/// Notes:
/// - Values are stored as JSONB so that callers can persist typed diffs.
/// - ordinal provides deterministic ordering of changes inside a single event.
/// - This table is append-only (guarded by DB triggers).
/// </summary>
public sealed class PlatformAuditEventChangesMigration : IDdlObject
{
    public string Name => "platform_audit_event_changes";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_audit_event_changes (
                                    audit_change_id UUID PRIMARY KEY,

                                    audit_event_id UUID NOT NULL,
                                    ordinal INTEGER NOT NULL,

                                    field_path TEXT NOT NULL,

                                    old_value_jsonb JSONB NULL,
                                    new_value_jsonb JSONB NULL,

                                    CONSTRAINT fk_platform_audit_event_changes_event
                                        FOREIGN KEY (audit_event_id)
                                            REFERENCES platform_audit_events(audit_event_id)
                                            ON UPDATE RESTRICT
                                            ON DELETE RESTRICT,

                                    CONSTRAINT ck_platform_audit_event_changes_ordinal_positive CHECK (ordinal > 0),
                                    CONSTRAINT ck_platform_audit_event_changes_field_path_nonempty CHECK (length(trim(field_path)) > 0),
                                    CONSTRAINT ck_platform_audit_event_changes_field_path_maxlen CHECK (length(field_path) <= 400)
                                );
                                """;
}
