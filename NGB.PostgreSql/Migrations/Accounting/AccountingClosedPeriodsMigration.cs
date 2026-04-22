using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingClosedPeriodsMigration : IDdlObject
{
    public string Name => "accounting_closed_periods";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS accounting_closed_periods (
                                    period DATE PRIMARY KEY,
                                    closed_at_utc TIMESTAMPTZ NOT NULL,
                                    closed_by TEXT NOT NULL,

                                    CONSTRAINT ck_closed_periods_month CHECK (EXTRACT(DAY FROM period) = 1)
                                );
                                """;
}
