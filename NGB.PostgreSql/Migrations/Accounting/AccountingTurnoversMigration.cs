using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingTurnoversMigration : IDdlObject
{
    public string Name => "accounting_turnovers";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS accounting_turnovers (
                                    period DATE NOT NULL,
                                    account_id UUID NOT NULL,
                                    dimension_set_id UUID NOT NULL,

                                    debit_amount  NUMERIC NOT NULL DEFAULT 0,
                                    credit_amount NUMERIC NOT NULL DEFAULT 0,

                                    PRIMARY KEY (period, account_id, dimension_set_id),

                                    CONSTRAINT chk_acc_turnovers_period_month_start
                                        CHECK (period = DATE_TRUNC('month', period)::date),

                                    CONSTRAINT fk_acc_turnovers_account
                                        FOREIGN KEY (account_id) REFERENCES accounting_accounts(account_id),

                                    CONSTRAINT fk_acc_turnovers_dimension_set
                                        FOREIGN KEY (dimension_set_id) REFERENCES platform_dimension_sets(dimension_set_id)
                                );
                                """;
}
