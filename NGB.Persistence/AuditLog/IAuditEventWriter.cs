using NGB.Core.AuditLog;

namespace NGB.Persistence.AuditLog;

public interface IAuditEventWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default);

    Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default);
}
