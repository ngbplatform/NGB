using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Catalogs;

public sealed class CatalogsMigration : IDdlObject
{
    public string Name => "catalogs";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS catalogs (
                                    id              uuid            NOT NULL PRIMARY KEY,
                                    catalog_code     text            NOT NULL,
                                    is_deleted       boolean         NOT NULL DEFAULT FALSE,
                                    created_at_utc   timestamptz     NOT NULL DEFAULT NOW(),
                                    updated_at_utc   timestamptz     NOT NULL DEFAULT NOW()
                                );

                                -- Timestamp default drift repair:
                                -- for timestamptz "instants" we prefer DEFAULT NOW() (not "NOW() at time zone 'UTC'").
                                DO $$
                                BEGIN
                                  IF EXISTS (
                                    SELECT 1
                                      FROM information_schema.columns
                                     WHERE table_schema = 'public'
                                       AND table_name   = 'catalogs'
                                       AND column_name  = 'created_at_utc'
                                  ) THEN
                                    EXECUTE 'ALTER TABLE catalogs ALTER COLUMN created_at_utc SET DEFAULT NOW()';
                                  END IF;

                                  IF EXISTS (
                                    SELECT 1
                                      FROM information_schema.columns
                                     WHERE table_schema = 'public'
                                       AND table_name   = 'catalogs'
                                       AND column_name  = 'updated_at_utc'
                                  ) THEN
                                    EXECUTE 'ALTER TABLE catalogs ALTER COLUMN updated_at_utc SET DEFAULT NOW()';
                                  END IF;
                                END $$;

                                -- NOTE:
                                -- Catalogs follow the same hybrid approach as documents:
                                -- - Common header lives in 'catalogs'
                                -- - Per-type data belongs in: cat_{catalog_code}, cat_{catalog_code}__{part}, ...
                                """;
}
