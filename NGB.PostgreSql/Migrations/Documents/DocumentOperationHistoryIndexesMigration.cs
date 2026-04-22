using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Documents;

public sealed class DocumentOperationHistoryIndexesMigration : IDdlObject
{
    public string Name => "platform_document_operation_history_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_platform_document_operation_state_started
                                    ON platform_document_operation_state(started_at_utc);

                                CREATE INDEX IF NOT EXISTS ix_platform_document_operation_state_completed
                                    ON platform_document_operation_state(completed_at_utc);

                                CREATE INDEX IF NOT EXISTS ix_platform_document_operation_history_document_occurred
                                    ON platform_document_operation_history(document_id, occurred_at_utc DESC, history_id DESC);

                                CREATE INDEX IF NOT EXISTS ix_platform_document_operation_history_attempt
                                    ON platform_document_operation_history(attempt_id, occurred_at_utc DESC, history_id DESC);
                                """;
}
