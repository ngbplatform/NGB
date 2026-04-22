using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingRegisterMigration : IDdlObject
{
    public string Name => "accounting_register_main";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS accounting_register_main (
                                    entry_id BIGSERIAL PRIMARY KEY,

                                    document_id UUID NOT NULL,
                                    period TIMESTAMPTZ NOT NULL,

                                    period_month DATE GENERATED ALWAYS AS (date_trunc('month', (period AT TIME ZONE 'UTC'))::date) STORED,

                                    debit_account_id  UUID NOT NULL,
                                    credit_account_id UUID NOT NULL,

                                    -- DimensionSetId is the long-term key for analytical dimensions.
                                    -- For empty dimensions it is Guid.Empty (no dimension set row is created).
                                    debit_dimension_set_id  UUID NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
                                    credit_dimension_set_id UUID NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',

                                    amount NUMERIC(18,4) NOT NULL,
                                    is_storno BOOLEAN NOT NULL DEFAULT FALSE,

                                    -- DB-level invariants
                                    CONSTRAINT ck_acc_reg_amount_positive CHECK (amount > 0),
                                    CONSTRAINT ck_acc_reg_debit_not_equal_credit CHECK (debit_account_id <> credit_account_id),

                                    CONSTRAINT fk_acc_reg_debit_account
                                        FOREIGN KEY (debit_account_id) REFERENCES accounting_accounts(account_id),

                                    CONSTRAINT fk_acc_reg_credit_account
                                        FOREIGN KEY (credit_account_id) REFERENCES accounting_accounts(account_id)
                                );
                                """;
}
