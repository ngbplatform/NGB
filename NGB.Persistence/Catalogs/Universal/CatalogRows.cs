namespace NGB.Persistence.Catalogs.Universal;

public sealed record CatalogHeadRow(
    Guid Id,
    bool IsMarkedForDeletion,
    string? Display,
    IReadOnlyDictionary<string, object?> Fields);

public sealed record CatalogLookupRow(Guid Id, string Label);

public sealed record CatalogLookupSearchRow(Guid Id, string CatalogCode, string? Label, bool IsMarkedForDeletion);
