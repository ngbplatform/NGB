using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingTurnoversIndexesMigration : IDdlObject
{
    public string Name => "accounting_turnovers_indexes";

    public string Generate() => """
                                DROP INDEX IF EXISTS ix_acc_turnovers_period_account;

                                -- pkey (period, account_id, dimension_set_id) covers most period scans.
                                -- Extra index helps account-centric reports (account card, drills):
                                CREATE INDEX IF NOT EXISTS ix_acc_turnovers_account_period
                                    ON accounting_turnovers (account_id, dimension_set_id, period);

                                """;
}
