using NGB.Contracts.Audit;
using NGB.Core.AuditLog;

namespace NGB.Application.Abstractions.Services;

public interface IAuditLogQueryService
{
    Task<AuditLogPageDto> GetEntityAuditLogAsync(
        AuditEntityKind entityKind,
        Guid entityId,
        DateTime? afterOccurredAtUtc,
        Guid? afterAuditEventId,
        int limit,
        CancellationToken ct);
}
