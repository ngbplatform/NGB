using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformDimensionSetItemsIndexesMigration : IDdlObject
{
    public string Name => "platform_dimension_set_items_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_platform_dimset_items_set
                                    ON platform_dimension_set_items(dimension_set_id);

                                CREATE INDEX IF NOT EXISTS ix_platform_dimset_items_dimension_value_set
                                    ON platform_dimension_set_items(dimension_id, value_id, dimension_set_id);
                                """;
}
