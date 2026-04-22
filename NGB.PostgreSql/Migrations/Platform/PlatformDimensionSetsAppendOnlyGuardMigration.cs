using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Append-only guards for platform_dimension_sets and platform_dimension_set_items.
///
/// Dimension sets are immutable snapshots: once a DimensionSetId is materialized, it must never change.
///
/// This reuses the shared guard function ngb_forbid_mutation_of_append_only_table
/// (defined by <see cref="PlatformAppendOnlyGuardFunctionMigration"/>).
/// </summary>
public sealed class PlatformDimensionSetsAppendOnlyGuardMigration : IDdlObject
{
    public string Name => "platform_dimension_sets_append_only_guard";

    public string Generate() => """
                                DO $$
                                BEGIN
                                    IF to_regclass('public.platform_dimension_sets') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_platform_dimension_sets_append_only ON public.platform_dimension_sets;
                                        CREATE TRIGGER trg_platform_dimension_sets_append_only
                                            BEFORE UPDATE OR DELETE ON public.platform_dimension_sets
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;

                                    IF to_regclass('public.platform_dimension_set_items') IS NOT NULL THEN
                                        DROP TRIGGER IF EXISTS trg_platform_dimension_set_items_append_only ON public.platform_dimension_set_items;
                                        CREATE TRIGGER trg_platform_dimension_set_items_append_only
                                            BEFORE UPDATE OR DELETE ON public.platform_dimension_set_items
                                            FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                                    END IF;
                                END
                                $$;
                                """;
}
