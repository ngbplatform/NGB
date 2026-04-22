namespace NGB.Core.AuditLog;

/// <summary>
/// Business audit event.
///
/// NOTE:
/// - This model is used for both writes and reads.
/// - When writing, <see cref="Changes"/> may be empty.
/// </summary>
public sealed record AuditEvent(
    Guid AuditEventId,
    AuditEntityKind EntityKind,
    Guid EntityId,
    string ActionCode,
    Guid? ActorUserId,
    DateTime OccurredAtUtc,
    Guid? CorrelationId,
    string? MetadataJson,
    IReadOnlyList<AuditFieldChange> Changes);
