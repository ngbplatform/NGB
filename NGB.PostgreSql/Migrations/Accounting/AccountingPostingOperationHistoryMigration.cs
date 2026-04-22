using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

/// <summary>
/// Immutable technical history for accounting posting attempts.
///
/// The mutable state table accounting_posting_state coordinates in-flight / dedupe semantics.
/// This append-only table preserves attempt facts (Started / Completed / Superseded).
/// </summary>
public sealed class AccountingPostingOperationHistoryMigration : IDdlObject
{
    public string Name => "accounting_posting_log_history";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS accounting_posting_log_history (
                                    history_id       uuid         PRIMARY KEY,
                                    attempt_id       uuid         NOT NULL,
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,
                                    event_kind       smallint     NOT NULL,
                                    occurred_at_utc  timestamptz  NOT NULL,

                                    CONSTRAINT ck_accounting_posting_log_history_operation
                                        CHECK (operation IN (1, 2, 3, 4)),

                                    CONSTRAINT ck_accounting_posting_log_history_event_kind
                                        CHECK (event_kind IN (1, 2, 3))
                                );

                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_log_history_document_operation_occurred
                                    ON accounting_posting_log_history(document_id, operation, occurred_at_utc DESC);

                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_log_history_attempt
                                    ON accounting_posting_log_history(attempt_id, occurred_at_utc);
                                

                                DO $$
                                BEGIN
                                    IF to_regclass('public.accounting_posting_log_history') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_accounting_posting_log_history_append_only ON public.accounting_posting_log_history;
                                        CREATE TRIGGER trg_accounting_posting_log_history_append_only
                                            BEFORE UPDATE OR DELETE ON public.accounting_posting_log_history
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;
                                END
                                $$;
                                """;
}
