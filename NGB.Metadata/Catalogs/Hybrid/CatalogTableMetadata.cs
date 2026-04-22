using NGB.Metadata.Base;

namespace NGB.Metadata.Catalogs.Hybrid;

public sealed record CatalogTableMetadata(
    string TableName,
    TableKind Kind,
    IReadOnlyList<CatalogColumnMetadata> Columns,
    IReadOnlyList<CatalogIndexMetadata> Indexes,
    string? PartCode = null);
