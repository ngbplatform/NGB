using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.AuditLog;

/// <summary>
/// Default runtime implementation of <see cref="IAuditLogService"/>.
///
/// The service participates in the current UnitOfWork transaction.
/// If persistence dependencies are not registered, it behaves as a strict no-op.
/// </summary>
internal sealed class AuditLogService(
    IUnitOfWork uow,
    ICurrentActorContext actorContext,
    ILogger<AuditLogService> logger,
    TimeProvider timeProvider,
    IPlatformUserRepository? users = null,
    IAuditEventWriter? writer = null)
    : IAuditLogService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task WriteAsync(
        AuditEntityKind entityKind,
        Guid entityId,
        string actionCode,
        IReadOnlyList<AuditFieldChange>? changes = null,
        object? metadata = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        if (writer is null)
            return; // AuditLog is not enabled.

        entityId.EnsureRequired(nameof(entityId));
        if (string.IsNullOrWhiteSpace(actionCode))
            throw new NgbArgumentRequiredException(nameof(actionCode));

        await WriteBatchCoreAsync(
            [
                new AuditLogWriteRequest(
                    entityKind,
                    entityId,
                    actionCode,
                    changes,
                    metadata,
                    correlationId)
            ],
            ct);
    }

    public async Task WriteBatchAsync(IReadOnlyList<AuditLogWriteRequest> requests, CancellationToken ct = default)
    {
        if (writer is null)
            return; // AuditLog is not enabled.

        if (requests is null)
            throw new NgbArgumentRequiredException(nameof(requests));

        if (requests.Count == 0)
            return;

        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            request.EntityId.EnsureRequired(nameof(requests));
            if (string.IsNullOrWhiteSpace(request.ActionCode))
                throw new NgbArgumentRequiredException(nameof(requests));
        }

        await WriteBatchCoreAsync(requests, ct);
    }

    private async Task WriteBatchCoreAsync(IReadOnlyList<AuditLogWriteRequest> requests, CancellationToken ct)
    {
        var auditWriter = writer
            ?? throw new NgbInvariantViolationException("Audit writer must be available when audit batching is executed.");

        // Business audit must be atomic with the business change.
        // If caller forgot to start a transaction, we prefer fail-fast.
        uow.EnsureActiveTransaction();

        Guid? actorUserId = null;
        var actor = actorContext.Current;
        
        if (actor is not null)
        {
            if (users is null)
            {
                logger.LogWarning("Audit actor is present but IPlatformUserRepository is not registered; actor_user_id will be NULL.");
            }
            else
            {
                actorUserId = await users.UpsertAsync(
                    authSubject: actor.AuthSubject,
                    email: actor.Email,
                    displayName: actor.DisplayName,
                    isActive: actor.IsActive,
                    ct);
            }
        }

        var nowUtc = timeProvider.GetUtcNowDateTime();
        nowUtc.EnsureUtc(nameof(nowUtc));

        var events = new AuditEvent[requests.Count];

        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];

            var metadataJson = request.Metadata is null
                ? null
                : JsonSerializer.Serialize(request.Metadata, Json);

            events[i] = new AuditEvent(
                AuditEventId: Guid.CreateVersion7(),
                EntityKind: request.EntityKind,
                EntityId: request.EntityId,
                ActionCode: request.ActionCode.Trim(),
                ActorUserId: actorUserId,
                OccurredAtUtc: nowUtc,
                CorrelationId: request.CorrelationId,
                MetadataJson: metadataJson,
                Changes: request.Changes ?? Array.Empty<AuditFieldChange>());
        }

        await auditWriter.WriteBatchAsync(events, ct);
    }

    public static AuditFieldChange Change(string fieldPath, object? oldValue, object? newValue)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
            throw new NgbArgumentRequiredException(nameof(fieldPath));

        return new AuditFieldChange(
            FieldPath: fieldPath.Trim(),
            OldValueJson: oldValue is null ? null : JsonSerializer.Serialize(oldValue, Json),
            NewValueJson: newValue is null ? null : JsonSerializer.Serialize(newValue, Json));
    }
}
