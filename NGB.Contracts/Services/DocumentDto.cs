using NGB.Contracts.Common;
using NGB.Contracts.Metadata;

namespace NGB.Contracts.Services;

public sealed record DocumentDto(
    Guid Id,
    string? Display,
    RecordPayload Payload,
    DocumentStatus Status,
    bool IsMarkedForDeletion,
    string? Number = null);

public sealed record DocumentLookupDto(
    Guid Id,
    string DocumentType,
    string? Display,
    DocumentStatus Status,
    bool IsMarkedForDeletion,
    string? Number = null);
