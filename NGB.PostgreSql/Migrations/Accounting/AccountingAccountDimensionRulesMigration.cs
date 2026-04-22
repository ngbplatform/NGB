using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

public sealed class AccountingAccountDimensionRulesMigration : IDdlObject
{
    public string Name => "accounting_account_dimension_rules";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS accounting_account_dimension_rules
                                (
                                    account_id   UUID NOT NULL,
                                    dimension_id UUID NOT NULL,
                                    ordinal      INT  NOT NULL,
                                    is_required  BOOLEAN NOT NULL,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT pk_acc_dim_rules PRIMARY KEY (account_id, dimension_id),

                                    CONSTRAINT ck_acc_dim_rules_ordinal_positive CHECK (ordinal > 0),

                                    CONSTRAINT fk_acc_dim_rules_account
                                        FOREIGN KEY (account_id)
                                        REFERENCES accounting_accounts(account_id)
                                        ON DELETE CASCADE,

                                    CONSTRAINT fk_acc_dim_rules_dimension
                                        FOREIGN KEY (dimension_id)
                                        REFERENCES platform_dimensions(dimension_id)
                                        ON DELETE RESTRICT
                                );

                                -- Critical DB guard: callers rely on specific, named foreign keys (for predictable SqlState/ConstraintName).
                                -- CREATE TABLE IF NOT EXISTS does not restore FKs after drift. Repair if missing or created under different names.
                                DO $$
                                DECLARE
                                    has_account boolean;
                                    has_dimension boolean;
                                    r record;
                                BEGIN
                                    SELECT EXISTS(
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'accounting_account_dimension_rules'::regclass
                                          AND conname = 'fk_acc_dim_rules_account'
                                    ) INTO has_account;

                                    SELECT EXISTS(
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conrelid = 'accounting_account_dimension_rules'::regclass
                                          AND conname = 'fk_acc_dim_rules_dimension'
                                    ) INTO has_dimension;

                                    IF has_account AND has_dimension THEN
                                        RETURN;
                                    END IF;

                                    FOR r IN
                                        SELECT conname
                                        FROM pg_constraint
                                        WHERE conrelid = 'accounting_account_dimension_rules'::regclass
                                          AND contype = 'f'
                                    LOOP
                                        EXECUTE format('ALTER TABLE accounting_account_dimension_rules DROP CONSTRAINT IF EXISTS %I', r.conname);
                                    END LOOP;

                                    ALTER TABLE accounting_account_dimension_rules
                                        ADD CONSTRAINT fk_acc_dim_rules_account
                                            FOREIGN KEY (account_id)
                                            REFERENCES accounting_accounts(account_id)
                                            ON DELETE CASCADE;

                                    ALTER TABLE accounting_account_dimension_rules
                                        ADD CONSTRAINT fk_acc_dim_rules_dimension
                                            FOREIGN KEY (dimension_id)
                                            REFERENCES platform_dimensions(dimension_id)
                                            ON DELETE RESTRICT;
                                END $$;
                                """;
}
