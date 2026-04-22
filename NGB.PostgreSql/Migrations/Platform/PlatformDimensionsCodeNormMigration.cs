using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Case-insensitive UX for platform_dimensions.code:
/// - Adds a generated normalized column: code_norm = lower(btrim(code))
/// - Enforces trimming constraints on code/name
///
/// Uniqueness is enforced by an index on code_norm WHERE is_deleted = false.
/// </summary>
public sealed class PlatformDimensionsCodeNormMigration : IDdlObject
{
    public string Name => "platform_dimensions_code_norm";

    public string Generate() => """
                                ALTER TABLE platform_dimensions
                                    ADD COLUMN IF NOT EXISTS code_norm TEXT GENERATED ALWAYS AS (lower(btrim(code))) STORED;

                                DO $$
                                BEGIN
                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_platform_dimensions_code_trimmed'
                                    ) THEN
                                        ALTER TABLE platform_dimensions
                                            ADD CONSTRAINT ck_platform_dimensions_code_trimmed
                                            CHECK (code = btrim(code));
                                    END IF;

                                    IF NOT EXISTS (
                                        SELECT 1
                                        FROM pg_constraint
                                        WHERE conname = 'ck_platform_dimensions_name_trimmed'
                                    ) THEN
                                        ALTER TABLE platform_dimensions
                                            ADD CONSTRAINT ck_platform_dimensions_name_trimmed
                                            CHECK (name = btrim(name));
                                    END IF;
                                END
                                $$;
                                """;
}
