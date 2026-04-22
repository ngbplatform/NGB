using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

/// <summary>
/// Ephemeral technical lifecycle state for document-level Post / Unpost / Repost coordination.
///
/// This table is NOT history. It holds only the current dedupe / in-progress row per
/// (document_id, operation). Immutable history lives in platform_document_operation_history.
/// </summary>
public sealed class DocumentOperationStateMigration : IDdlObject
{
    public string Name => "platform_document_operation_state";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_document_operation_state (
                                    document_id      uuid         NOT NULL,
                                    operation        smallint     NOT NULL,
                                    attempt_id       uuid         NOT NULL,
                                    started_at_utc   timestamptz  NOT NULL,
                                    completed_at_utc timestamptz  NULL,

                                    CONSTRAINT pk_platform_document_operation_state
                                        PRIMARY KEY (document_id, operation),

                                    CONSTRAINT fk_platform_document_operation_state_document
                                        FOREIGN KEY (document_id) REFERENCES documents(id),

                                    -- Allowed operations (see NGB.Accounting.PostingState.PostingOperation):
                                    -- 1 = Post, 2 = Unpost, 3 = Repost, 4 = CloseFiscalYear
                                    CONSTRAINT ck_platform_document_operation_state_operation
                                        CHECK (operation IN (1, 2, 3, 4)),

                                    CONSTRAINT ck_platform_document_operation_state_completed_after_started
                                        CHECK (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)
                                );
                                """;
}
