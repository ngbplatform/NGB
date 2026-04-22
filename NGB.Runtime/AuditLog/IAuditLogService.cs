using NGB.Core.AuditLog;

namespace NGB.Runtime.AuditLog;

public interface IAuditLogService
{
    Task WriteAsync(
        AuditEntityKind entityKind,
        Guid entityId,
        string actionCode,
        IReadOnlyList<AuditFieldChange>? changes = null,
        object? metadata = null,
        Guid? correlationId = null,
        CancellationToken ct = default);

    Task WriteBatchAsync(IReadOnlyList<AuditLogWriteRequest> requests, CancellationToken ct = default);
}

public sealed record AuditLogWriteRequest(
    AuditEntityKind EntityKind,
    Guid EntityId,
    string ActionCode,
    IReadOnlyList<AuditFieldChange>? Changes = null,
    object? Metadata = null,
    Guid? CorrelationId = null);
