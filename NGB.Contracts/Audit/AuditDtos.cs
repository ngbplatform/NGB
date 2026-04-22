namespace NGB.Contracts.Audit;

public sealed record AuditFieldChangeDto(string FieldPath, string? OldValueJson, string? NewValueJson);

public sealed record AuditActorDto(Guid? UserId, string? DisplayName, string? Email);

public sealed record AuditEventDto(
    Guid AuditEventId,
    short EntityKind,
    Guid EntityId,
    string ActionCode,
    AuditActorDto? Actor,
    DateTime OccurredAtUtc,
    Guid? CorrelationId,
    string? MetadataJson,
    IReadOnlyList<AuditFieldChangeDto> Changes);

public sealed record AuditCursorDto(DateTime OccurredAtUtc, Guid AuditEventId);

public sealed record AuditLogPageDto(IReadOnlyList<AuditEventDto> Items, AuditCursorDto? NextCursor, int Limit);
