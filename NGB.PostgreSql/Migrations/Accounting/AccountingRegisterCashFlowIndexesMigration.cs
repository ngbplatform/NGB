using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingRegisterCashFlowIndexesMigration : IDdlObject
{
    public string Name => "accounting_register_cash_flow_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_cash_flow_debit_cash_period_counter
                                    ON accounting_register_main(debit_account_id, period, credit_account_id);

                                CREATE INDEX IF NOT EXISTS ix_acc_reg_cash_flow_credit_cash_period_counter
                                    ON accounting_register_main(credit_account_id, period, debit_account_id);
                                """;
}
