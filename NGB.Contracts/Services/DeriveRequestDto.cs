using NGB.Contracts.Common;

namespace NGB.Contracts.Services;

public sealed record DeriveRequestDto(
    Guid SourceDocumentId,
    string RelationshipType,
    RecordPayload? InitialPayload = null);
