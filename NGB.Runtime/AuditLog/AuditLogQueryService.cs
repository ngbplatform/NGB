using NGB.Application.Abstractions.Services;
using NGB.Contracts.Audit;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.AuditLog;

internal sealed class AuditLogQueryService(IAuditEventReader reader, IPlatformUserRepository? platformUsers = null)
    : IAuditLogQueryService
{
    public async Task<AuditLogPageDto> GetEntityAuditLogAsync(
        AuditEntityKind entityKind,
        Guid entityId,
        DateTime? afterOccurredAtUtc,
        Guid? afterAuditEventId,
        int limit,
        CancellationToken ct)
    {
        if (entityId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(entityId), "EntityId must be a non-empty GUID.");

        if (afterOccurredAtUtc.HasValue)
            afterOccurredAtUtc.Value.EnsureUtc(nameof(afterOccurredAtUtc));

        if (limit <= 0 || limit > 100)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be between 1 and 100.");

        var query = new AuditLogQuery(
            EntityKind: entityKind,
            EntityId: entityId,
            AfterOccurredAtUtc: afterOccurredAtUtc,
            AfterAuditEventId: afterAuditEventId,
            Limit: limit,
            Offset: 0);

        var events = await reader.QueryAsync(query, ct);

        IReadOnlyDictionary<Guid, PlatformUser> users = new Dictionary<Guid, PlatformUser>();
        if (platformUsers is not null)
        {
            var actorIds = events
                .Select(x => x.ActorUserId)
                .Where(x => x.HasValue && x.Value != Guid.Empty)
                .Select(x => x!.Value)
                .Distinct()
                .ToArray();

            if (actorIds.Length > 0)
                users = await platformUsers.GetByIdsAsync(actorIds, ct);
        }

        var items = events
            .Select(x => new AuditEventDto(
                x.AuditEventId,
                (short)x.EntityKind,
                x.EntityId,
                x.ActionCode,
                ToActorDto(x.ActorUserId, users),
                x.OccurredAtUtc,
                x.CorrelationId,
                x.MetadataJson,
                x.Changes.Select(c => new AuditFieldChangeDto(c.FieldPath, c.OldValueJson, c.NewValueJson)).ToArray()))
            .ToArray();

        var last = items.LastOrDefault();
        var nextCursor = last is null ? null : new AuditCursorDto(last.OccurredAtUtc, last.AuditEventId);
        return new AuditLogPageDto(items, nextCursor, limit);
    }

    private static AuditActorDto? ToActorDto(Guid? actorUserId, IReadOnlyDictionary<Guid, PlatformUser> users)
    {
        if (!actorUserId.HasValue)
            return null;

        if (users.TryGetValue(actorUserId.Value, out var user))
            return new AuditActorDto(user.UserId, user.DisplayName, user.Email);

        return new AuditActorDto(actorUserId.Value, null, null);
    }
}
