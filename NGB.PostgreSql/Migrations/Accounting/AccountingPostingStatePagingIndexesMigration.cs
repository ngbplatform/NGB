using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingPostingStatePagingIndexesMigration : IDdlObject
{
    public string Name => "accounting_posting_state_paging_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_page_order
                                    ON accounting_posting_state(started_at_utc DESC, document_id DESC, operation DESC);

                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_operation_page_order
                                    ON accounting_posting_state(operation, started_at_utc DESC, document_id DESC);

                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_incomplete_page_order
                                    ON accounting_posting_state(started_at_utc DESC, document_id DESC, operation DESC)
                                    WHERE completed_at_utc IS NULL;
                                """;
}
