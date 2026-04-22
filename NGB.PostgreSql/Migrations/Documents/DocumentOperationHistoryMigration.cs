using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

/// <summary>
/// Immutable append-only history of document lifecycle state events.
///
/// Event kinds:
/// - 1 = Started
/// - 2 = Completed
/// - 3 = Superseded (stale in-progress attempt was taken over)
/// </summary>
public sealed class DocumentOperationHistoryMigration : IDdlObject
{
    public string Name => "platform_document_operation_history";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_document_operation_history (
                                    history_id       uuid         PRIMARY KEY,
                                    attempt_id       uuid         NOT NULL,
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,
                                    event_kind       smallint     NOT NULL,
                                    occurred_at_utc  timestamptz  NOT NULL,

                                    CONSTRAINT fk_platform_document_operation_history_document
                                        FOREIGN KEY (document_id) REFERENCES documents(id),

                                    CONSTRAINT ck_platform_document_operation_history_operation
                                        CHECK (operation IN (1, 2, 3, 4)),

                                    CONSTRAINT ck_platform_document_operation_history_event_kind
                                        CHECK (event_kind IN (1, 2, 3))
                                );

                                DO $$
                                BEGIN
                                    IF to_regclass('public.platform_document_operation_history') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_platform_document_operation_history_append_only ON public.platform_document_operation_history;
                                        CREATE TRIGGER trg_platform_document_operation_history_append_only
                                            BEFORE UPDATE OR DELETE ON public.platform_document_operation_history
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;
                                END
                                $$;
                                """;
}
