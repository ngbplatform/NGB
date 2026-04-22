namespace NGB.Core.AuditLog;

/// <summary>
/// Query object for reading audit events.
///
/// Notes:
/// - Results are ordered by (OccurredAtUtc DESC, AuditEventId DESC).
/// - For production paging, prefer cursor-based paging via AfterOccurredAtUtc + AfterAuditEventId.
///   Offset-based paging is supported for convenience but may become slow for deep pages.
/// </summary>
public sealed record AuditLogQuery(
    AuditEntityKind? EntityKind = null,
    Guid? EntityId = null,
    Guid? ActorUserId = null,
    string? ActionCode = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    DateTime? AfterOccurredAtUtc = null,
    Guid? AfterAuditEventId = null,
    int Limit = 200,
    int Offset = 0);
