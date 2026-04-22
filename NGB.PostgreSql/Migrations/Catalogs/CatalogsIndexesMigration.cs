using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Catalogs;

public sealed class CatalogsIndexesMigration : IDdlObject
{
    public string Name => "catalogs_indexes";

    public string Generate() => """
                                CREATE INDEX IF NOT EXISTS ix_catalogs_catalog_code
                                    ON catalogs (catalog_code);

                                CREATE INDEX IF NOT EXISTS ix_catalogs_catalog_code_not_deleted
                                    ON catalogs (catalog_code)
                                    WHERE is_deleted = FALSE;
                                """;
}
