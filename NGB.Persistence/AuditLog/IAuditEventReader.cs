using NGB.Core.AuditLog;

namespace NGB.Persistence.AuditLog;

public interface IAuditEventReader
{
    Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditLogQuery query, CancellationToken ct = default);
}
