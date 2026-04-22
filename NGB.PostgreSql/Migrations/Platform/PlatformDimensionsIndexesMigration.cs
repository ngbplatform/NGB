using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

public sealed class PlatformDimensionsIndexesMigration : IDdlObject
{
    public string Name => "platform_dimensions_indexes";

    public string Generate() => """
                                CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_dimensions_code_norm_not_deleted
                                    ON platform_dimensions(code_norm);

                                CREATE INDEX IF NOT EXISTS ix_platform_dimensions_is_active
                                    ON platform_dimensions(is_active)
                                    WHERE is_deleted = FALSE;
                                """;
}
