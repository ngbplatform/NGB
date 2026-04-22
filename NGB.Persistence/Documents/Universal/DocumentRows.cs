using NGB.Core.Documents;

namespace NGB.Persistence.Documents.Universal;

public sealed record DocumentHeadRow(
    Guid Id,
    DocumentStatus Status,
    bool IsMarkedForDeletion,
    string? Display,
    IReadOnlyDictionary<string, object?> Fields,
    string? Number = null);

public sealed record DocumentLookupRow(
    Guid Id,
    string TypeCode,
    DocumentStatus Status,
    bool IsMarkedForDeletion,
    string? Label,
    string? Number = null);
