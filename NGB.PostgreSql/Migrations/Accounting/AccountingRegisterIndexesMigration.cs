using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingRegisterIndexesMigration : IDdlObject
{
    public string Name => "accounting_register_main_indexes";

    public string Generate() => """
                                -- document lookups (reposting / storno)
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_document_id
                                    ON accounting_register_main(document_id);


                                -- Faster Unpost/Repost lookups: select original (non-storno) rows for a document
                                -- and derive affected months without scanning storno rows.
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_document_month_nostorno
                                    ON accounting_register_main (document_id, period_month)
                                    WHERE is_storno = FALSE;

                                -- month slicing (reports/closing)
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_period_month
                                    ON accounting_register_main(period_month);

                                -- account + month (account card / turnovers by account)
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_debit_account_month
                                    ON accounting_register_main(debit_account_id, period_month);

                                CREATE INDEX IF NOT EXISTS ix_acc_reg_credit_account_month
                                    ON accounting_register_main(credit_account_id, period_month);

                                -- account + month + dimension set (dimension-aware reports)
                                CREATE INDEX IF NOT EXISTS ix_acc_reg_debit_month_dimset
                                    ON accounting_register_main(period_month, debit_account_id, debit_dimension_set_id);

                                CREATE INDEX IF NOT EXISTS ix_acc_reg_credit_month_dimset
                                    ON accounting_register_main(period_month, credit_account_id, credit_dimension_set_id);
                                -- Optional (only if you often filter by both account and dimension set)

                                -- Optional for huge tables (append-only):
                                -- BRIN is extremely small and fast for time-range scans if data is inserted in time order.
                                CREATE INDEX IF NOT EXISTS brin_acc_reg_period
                                    ON accounting_register_main USING BRIN(period);
                                """;
}
