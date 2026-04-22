using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Drift-repair: ensure platform_dimension_set_items has the critical CHECK constraints that
/// forbid Guid.Empty in key columns.
///
/// Why:
/// - Integration tests (and production invariants) assume these constraints exist.
/// - Some schema-validation tests intentionally DROP constraints to simulate drift.
/// - Most migrations are CREATE TABLE IF NOT EXISTS and won't re-add dropped constraints.
///
/// This migration is idempotent and safely re-adds constraints when missing.
/// </summary>
public sealed class PlatformDimensionSetItemsConstraintsDriftRepairMigration : IDdlObject
{
    public string Name => "platform_dimension_set_items_constraints_drift_repair";

    public string Generate() => """
                                DO $$
                                BEGIN
                                    IF to_regclass('public.platform_dimension_set_items') IS NULL THEN
                                        RETURN;
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_platform_dimset_items_set_nonempty'
                                    ) THEN
                                        ALTER TABLE public.platform_dimension_set_items
                                            ADD CONSTRAINT ck_platform_dimset_items_set_nonempty
                                                CHECK (dimension_set_id <> '00000000-0000-0000-0000-000000000000');
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_platform_dimset_items_dimension_nonempty'
                                    ) THEN
                                        ALTER TABLE public.platform_dimension_set_items
                                            ADD CONSTRAINT ck_platform_dimset_items_dimension_nonempty
                                                CHECK (dimension_id <> '00000000-0000-0000-0000-000000000000');
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_platform_dimset_items_value_nonempty'
                                    ) THEN
                                        ALTER TABLE public.platform_dimension_set_items
                                            ADD CONSTRAINT ck_platform_dimset_items_value_nonempty
                                                CHECK (value_id <> '00000000-0000-0000-0000-000000000000');
                                    END IF;
                                END
                                $$;
                                """;
}
