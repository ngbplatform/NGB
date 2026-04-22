using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingBalancesMigration : IDdlObject
{
    public string Name => "accounting_balances";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS accounting_balances (
                                    period DATE NOT NULL,
                                    account_id UUID NOT NULL,
                                    dimension_set_id UUID NOT NULL,

                                    opening_balance NUMERIC NOT NULL DEFAULT 0,
                                    closing_balance NUMERIC NOT NULL DEFAULT 0,

                                    PRIMARY KEY (period, account_id, dimension_set_id),

                                    CONSTRAINT chk_acc_balances_period_month_start
                                        CHECK (period = DATE_TRUNC('month', period)::date),

                                    CONSTRAINT fk_acc_balances_account
                                        FOREIGN KEY (account_id) REFERENCES accounting_accounts(account_id),

                                    CONSTRAINT fk_acc_balances_dimension_set
                                        FOREIGN KEY (dimension_set_id) REFERENCES platform_dimension_sets(dimension_set_id)
                                );
                                """;
}
