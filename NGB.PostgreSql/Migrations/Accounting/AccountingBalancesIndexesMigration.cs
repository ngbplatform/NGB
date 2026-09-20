using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingBalancesIndexesMigration : IDdlObject
{
    public string Name => "accounting_balances_indexes";

    public string Generate() => """
                                -- pkey (period, account_id, dimension_set_id) covers most period scans.
                                -- Extra index helps account-centric reads.
                                CREATE INDEX IF NOT EXISTS ix_acc_balances_account_period
                                    ON accounting_balances (account_id, dimension_set_id, period);

                                CREATE INDEX IF NOT EXISTS ix_acc_balances_period_account
                                    ON accounting_balances (period, account_id, dimension_set_id);
                                """;
}
