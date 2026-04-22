using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Accounting;

/// <summary>
/// Drift-repair: ensure accounting_register_main has FKs to platform_dimension_sets for debit/credit DimensionSetId columns.
/// 
/// Why:
/// - register rows store debit_dimension_set_id / credit_dimension_set_id and default to Guid.Empty.
/// - platform_dimension_sets contains a reserved Guid.Empty row.
/// - FK contract prevents inserting invalid DimensionSetId values and keeps read-side joins safe.
/// </summary>
public sealed class AccountingRegisterDimensionSetForeignKeysMigration : IDdlObject
{
    public string Name => "accounting_register_main_dimension_set_fks";

    public string Generate() => """
                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'fk_acc_reg_debit_dimension_set'
                                    ) THEN
                                        ALTER TABLE accounting_register_main
                                            ADD CONSTRAINT fk_acc_reg_debit_dimension_set
                                                FOREIGN KEY (debit_dimension_set_id)
                                                REFERENCES platform_dimension_sets(dimension_set_id)
                                                ON DELETE RESTRICT;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'fk_acc_reg_credit_dimension_set'
                                    ) THEN
                                        ALTER TABLE accounting_register_main
                                            ADD CONSTRAINT fk_acc_reg_credit_dimension_set
                                                FOREIGN KEY (credit_dimension_set_id)
                                                REFERENCES platform_dimension_sets(dimension_set_id)
                                                ON DELETE RESTRICT;
                                    END IF;
                                END
                                $$;
                                """;
}
