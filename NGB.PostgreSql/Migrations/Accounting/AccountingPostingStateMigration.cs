using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

/// <summary>
/// Ephemeral idempotency state for accounting posting operations.
///
/// One row per (document_id, operation). The row is written in the same transaction
/// as register/turnovers writes so that retries after a timeout are safe.
///
/// IMPORTANT:
/// - This table is technical mutable state, not immutable history.
/// - Immutable attempt history lives in accounting_posting_log_history.
///
/// Operation is stored as SMALLINT for compactness and to prevent invalid values.
/// </summary>
public sealed class AccountingPostingStateMigration : IDdlObject
{
    public string Name => "accounting_posting_state";

    public string Generate() => """
                                DO $$
                                BEGIN
                                    IF to_regclass('public.accounting_posting_state') IS NULL
                                       AND to_regclass('public.accounting_posting_log') IS NOT NULL THEN
                                        ALTER TABLE accounting_posting_log RENAME TO accounting_posting_state;
                                    END IF;
                                END
                                $$;

                                CREATE TABLE IF NOT EXISTS accounting_posting_state (
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,

                                    attempt_id       uuid         NULL,
                                    started_at_utc   timestamptz  NOT NULL,
                                    completed_at_utc timestamptz  NULL,

                                    CONSTRAINT pk_accounting_posting_state PRIMARY KEY (document_id, operation),

                                    -- Allowed operations (see NGB.Persistence.PostingState.PostingOperation):
                                    -- 1 = Post, 2 = Unpost, 3 = Repost, 4 = CloseFiscalYear
                                    CONSTRAINT ck_accounting_posting_state_operation
                                        CHECK (operation IN (1, 2, 3, 4)),

                                    CONSTRAINT ck_accounting_posting_state_completed_after_started
                                        CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
                                );

                                ALTER TABLE accounting_posting_state
                                    ADD COLUMN IF NOT EXISTS attempt_id uuid;
                                """;
}
