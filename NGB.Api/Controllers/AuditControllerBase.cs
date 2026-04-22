using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Audit;
using NGB.Core.AuditLog;

namespace NGB.Api.Controllers;

public abstract class AuditControllerBase(IAuditLogQueryService service) : ControllerBase
{
    [HttpGet("~/api/audit/entities/{entityKind}/{entityId:guid}")]
    public Task<AuditLogPageDto> GetEntityAuditLog(
        [FromRoute] AuditEntityKind entityKind,
        [FromRoute] Guid entityId,
        [FromQuery] DateTime? afterOccurredAtUtc,
        [FromQuery] Guid? afterAuditEventId,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => service.GetEntityAuditLogAsync(entityKind, entityId, afterOccurredAtUtc, afterAuditEventId, limit, ct);
}
