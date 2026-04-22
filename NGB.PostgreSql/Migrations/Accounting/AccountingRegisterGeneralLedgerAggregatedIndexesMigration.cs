using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingRegisterGeneralLedgerAggregatedIndexesMigration : IDdlObject
{
    public string Name => "accounting_register_main_general_ledger_aggregated_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_gl_agg_debit_group_page_order
                                    ON accounting_register_main(debit_account_id, period, document_id, credit_account_id, debit_dimension_set_id);

                                CREATE INDEX IF NOT EXISTS ix_acc_reg_gl_agg_credit_group_page_order
                                    ON accounting_register_main(credit_account_id, period, document_id, debit_account_id, credit_dimension_set_id);
                                """;
}
