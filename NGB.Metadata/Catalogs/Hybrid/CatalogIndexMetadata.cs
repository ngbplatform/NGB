namespace NGB.Metadata.Catalogs.Hybrid;

public sealed record CatalogIndexMetadata(string Name, IReadOnlyList<string> ColumnNames, bool Unique = false);
