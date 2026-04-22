using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingPostingStateIndexesMigration : IDdlObject
{
    public string Name => "accounting_posting_state_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_operation
                                    ON accounting_posting_state(operation);

                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_started
                                    ON accounting_posting_state(started_at_utc);

                                CREATE INDEX IF NOT EXISTS ix_accounting_posting_state_completed
                                    ON accounting_posting_state(completed_at_utc);
                                """;
}
