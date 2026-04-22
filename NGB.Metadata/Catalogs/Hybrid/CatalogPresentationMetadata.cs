namespace NGB.Metadata.Catalogs.Hybrid;

/// <summary>
/// How to represent a catalog item (used by enrichment/read models and metadata-driven UI).
/// </summary>
public sealed record CatalogPresentationMetadata(
    string TableName,
    string DisplayColumn,
    bool ComputedDisplay = false);
