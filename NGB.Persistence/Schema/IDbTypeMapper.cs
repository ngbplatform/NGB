using NGB.Metadata.Base;

namespace NGB.Persistence.Schema;

/// <summary>
/// Maps provider-agnostic logical column types (see <see cref="LogicalDbType"/>) to provider-specific database types
/// and validates compatibility against inspected schema.
/// </summary>
public interface IDbTypeMapper
{
    /// <summary>Provider name (e.g., "PostgreSQL"). Used for diagnostics only.</summary>
    string Provider { get; }

    /// <summary>Returns a canonical expected provider type name for diagnostics.</summary>
    string GetExpectedDbType(ColumnType logicalType);

    /// <summary>Returns true if <paramref name="actualDbType"/> is compatible with <paramref name="logicalType"/>.</summary>
    bool IsCompatible(ColumnType logicalType, string actualDbType);
}
