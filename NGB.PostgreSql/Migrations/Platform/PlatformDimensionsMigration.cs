using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Platform dimension definitions.
///
/// A "dimension" is a named analytical axis (e.g., Department, Project, Property, Customer).
/// Dimension values are typically catalog entities (ValueId points to an entity_id).
///
/// NOTE:
/// - This table is NOT append-only: dimensions may be renamed, deactivated, or soft-deleted.
/// - Dimension sets (mapping to DimensionSetId) are stored separately and are append-only.
/// </summary>
public sealed class PlatformDimensionsMigration : IDdlObject
{
    public string Name => "platform_dimensions";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_dimensions (
                                    dimension_id UUID PRIMARY KEY,

                                    code TEXT NOT NULL,
                                    name TEXT NOT NULL,

                                    is_active BOOLEAN NOT NULL DEFAULT TRUE,
                                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                                    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),

                                    CONSTRAINT ck_platform_dimensions_code_nonempty CHECK (length(trim(code)) > 0),
                                    CONSTRAINT ck_platform_dimensions_name_nonempty CHECK (length(trim(name)) > 0)
                                );
                                """;
}
