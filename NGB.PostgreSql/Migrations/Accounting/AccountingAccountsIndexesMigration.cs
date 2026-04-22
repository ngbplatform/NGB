using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingAccountsIndexesMigration : IDdlObject
{
    public string Name => "accounting_accounts_indexes";

    public string Generate() => """
                                -- Unique normalized code (case-insensitive, trim+lower) across ALL accounts (including deleted)
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_acc_accounts_code_norm
                                    ON accounting_accounts(code_norm);

                                CREATE INDEX IF NOT EXISTS ix_acc_accounts_is_active
                                    ON accounting_accounts(is_active)
                                    WHERE is_deleted = FALSE;
                                """;
}
