using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Defense in depth: business audit tables are append-only.
/// UPDATE/DELETE are forbidden to guarantee immutability of the audit trail.
/// </summary>
public sealed class PlatformAuditAppendOnlyGuardMigration : IDdlObject
{
    public string Name => "platform_audit_append_only_guard";

    public string Generate() => """
                                DO $$
                                BEGIN
                                    IF to_regclass('public.platform_audit_events') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_platform_audit_events_append_only ON public.platform_audit_events;
                                        CREATE TRIGGER trg_platform_audit_events_append_only
                                            BEFORE UPDATE OR DELETE ON public.platform_audit_events
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;

                                    IF to_regclass('public.platform_audit_event_changes') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_platform_audit_event_changes_append_only ON public.platform_audit_event_changes;
                                        CREATE TRIGGER trg_platform_audit_event_changes_append_only
                                            BEFORE UPDATE OR DELETE ON public.platform_audit_event_changes
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;
                                END
                                $$;
                                """;
}
