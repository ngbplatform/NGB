using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingAccountsCashFlowMetadataMigration : IDdlObject
{
    public string Name => "accounting_accounts_cash_flow_metadata";

    public string Generate() => """
                                ALTER TABLE accounting_accounts
                                    ADD COLUMN IF NOT EXISTS cash_flow_role SMALLINT NOT NULL DEFAULT 0;

                                ALTER TABLE accounting_accounts
                                    ADD COLUMN IF NOT EXISTS cash_flow_line_code TEXT NULL;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_acc_accounts_cash_flow_role_range'
                                    ) THEN
                                        ALTER TABLE accounting_accounts
                                            ADD CONSTRAINT ck_acc_accounts_cash_flow_role_range
                                            CHECK (cash_flow_role BETWEEN 0 AND 5);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_acc_accounts_cash_flow_line_code_trimmed'
                                    ) THEN
                                        ALTER TABLE accounting_accounts
                                            ADD CONSTRAINT ck_acc_accounts_cash_flow_line_code_trimmed
                                            CHECK (cash_flow_line_code IS NULL OR cash_flow_line_code = btrim(cash_flow_line_code));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_acc_accounts_cash_flow_line_code_nonempty'
                                    ) THEN
                                        ALTER TABLE accounting_accounts
                                            ADD CONSTRAINT ck_acc_accounts_cash_flow_line_code_nonempty
                                            CHECK (cash_flow_line_code IS NULL OR length(cash_flow_line_code) > 0);
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'fk_acc_accounts_cash_flow_line_code'
                                    ) THEN
                                        ALTER TABLE accounting_accounts
                                            ADD CONSTRAINT fk_acc_accounts_cash_flow_line_code
                                            FOREIGN KEY (cash_flow_line_code)
                                            REFERENCES accounting_cash_flow_lines(line_code);
                                    END IF;
                                END
                                $$;
                                """;
}
