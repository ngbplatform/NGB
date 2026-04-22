using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingRegisterAccountCardPagingIndexesMigration : IDdlObject
{
    public string Name => "accounting_register_main_account_card_paging_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_account_card_debit_dim_page_order
                                    ON accounting_register_main(debit_account_id, debit_dimension_set_id, period, entry_id);

                                CREATE INDEX IF NOT EXISTS ix_acc_reg_account_card_credit_dim_page_order
                                    ON accounting_register_main(credit_account_id, credit_dimension_set_id, period, entry_id);
                                """;
}
