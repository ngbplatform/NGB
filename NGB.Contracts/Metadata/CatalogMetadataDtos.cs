namespace NGB.Contracts.Metadata;

public sealed record CatalogCapabilitiesDto(
    bool CanCreate = true,
    bool CanEdit = true,
    bool CanDelete = true,
    bool CanMarkForDeletion = true);

public sealed record CatalogTypeMetadataDto(
    string CatalogType,
    string DisplayName,
    EntityKind Kind,
    string? Icon = null,
    ListMetadataDto? List = null,
    FormMetadataDto? Form = null,
    IReadOnlyList<PartMetadataDto>? Parts = null,
    CatalogCapabilitiesDto? Capabilities = null);
