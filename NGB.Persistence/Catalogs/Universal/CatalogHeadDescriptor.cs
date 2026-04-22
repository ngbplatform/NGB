using NGB.Metadata.Base;

namespace NGB.Persistence.Catalogs.Universal;

/// <summary>
/// Describes a catalog type head table (cat_*) for universal, metadata-driven CRUD.
///
/// Identifiers (table/column names) must come from trusted metadata (Definitions).
/// Implementations must quote identifiers defensively.
/// </summary>
public sealed record CatalogHeadDescriptor(
    string CatalogCode,
    string HeadTableName,
    string DisplayColumn,
    IReadOnlyList<CatalogHeadColumn> Columns);

public sealed record CatalogHeadColumn(string ColumnName, ColumnType ColumnType);
