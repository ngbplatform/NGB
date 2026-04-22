using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>
/// Canonical dimension set headers.
///
/// A dimension set is identified by a deterministic UUID (DimensionSetId) computed from its items.
/// This table exists mainly for:
/// - referential integrity
/// - reporting (join point)
/// - append-only invariants
///
/// SPECIAL:
/// - Guid.Empty is reserved for the empty set.
/// </summary>
public sealed class PlatformDimensionSetsMigration : IDdlObject
{
    public string Name => "platform_dimension_sets";

    public string Generate() => """
                                CREATE TABLE IF NOT EXISTS platform_dimension_sets (
                                    dimension_set_id UUID PRIMARY KEY,

                                    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
                                );

                                -- Reserved empty set (no dimensions)
                                INSERT INTO platform_dimension_sets (dimension_set_id)
                                VALUES ('00000000-0000-0000-0000-000000000000')
                                ON CONFLICT (dimension_set_id) DO NOTHING;
                                """;
}
