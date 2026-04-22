using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingRegisterGeneralJournalPagingIndexesMigration : IDdlObject
{
    public string Name => "accounting_register_main_general_journal_paging_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_general_journal_page_order
                                    ON accounting_register_main(period, entry_id);

                                CREATE INDEX IF NOT EXISTS ix_acc_reg_general_journal_document_page_order
                                    ON accounting_register_main(document_id, period, entry_id);

                                CREATE INDEX IF NOT EXISTS ix_acc_reg_general_journal_debit_page_order
                                    ON accounting_register_main(debit_account_id, period, entry_id);

                                CREATE INDEX IF NOT EXISTS ix_acc_reg_general_journal_credit_page_order
                                    ON accounting_register_main(credit_account_id, period, entry_id);
                                """;
}
