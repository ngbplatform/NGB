using NGB.Metadata.Base;

namespace NGB.Metadata.Catalogs.Hybrid;

public sealed record CatalogColumnMetadata(
    string ColumnName,
    ColumnType ColumnType,
    bool Required = false,
    int? MaxLength = null,
    string? UiLabel = null,
    LookupSourceMetadata? Lookup = null,
    IReadOnlyList<FieldOptionMetadata>? Options = null);
