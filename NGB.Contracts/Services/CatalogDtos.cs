using NGB.Contracts.Common;

namespace NGB.Contracts.Services;

public sealed record CatalogItemDto(
    Guid Id,
    string? Display,
    RecordPayload Payload,
    bool IsMarkedForDeletion,
    bool IsDeleted);

public sealed record LookupItemDto(Guid Id, string Label, IReadOnlyDictionary<string, string>? Meta = null);

public sealed record CatalogLookupDto(Guid Id, string CatalogType, string? Display, bool IsMarkedForDeletion);
