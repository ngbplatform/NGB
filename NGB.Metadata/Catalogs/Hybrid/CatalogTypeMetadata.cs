namespace NGB.Metadata.Catalogs.Hybrid;

public sealed record CatalogTypeMetadata(
    string CatalogCode,
    string DisplayName,
    IReadOnlyList<CatalogTableMetadata> Tables,
    CatalogPresentationMetadata Presentation,
    CatalogMetadataVersion Version);
