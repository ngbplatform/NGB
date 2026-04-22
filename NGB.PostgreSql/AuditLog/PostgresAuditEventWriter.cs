using Dapper;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.AuditLog;

public sealed class PostgresAuditEventWriter(IUnitOfWork uow) : IAuditEventWriter
{
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        if (auditEvent is null)
            throw new NgbArgumentRequiredException(nameof(auditEvent));

        var normalized = NormalizeAuditEvent(auditEvent, nameof(auditEvent));
        await WriteBatchCoreAsync([normalized], ct);
    }

    public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
    {
        if (auditEvents is null)
            throw new NgbArgumentRequiredException(nameof(auditEvents));

        if (auditEvents.Count == 0)
            return;

        var normalizedEvents = new AuditEvent[auditEvents.Count];
        for (var i = 0; i < auditEvents.Count; i++)
        {
            var auditEvent = auditEvents[i];
            if (auditEvent is null)
                throw new NgbArgumentRequiredException(nameof(auditEvents));

            normalizedEvents[i] = NormalizeAuditEvent(auditEvent, nameof(auditEvents));
        }

        await WriteBatchCoreAsync(normalizedEvents, ct);
    }

    private async Task WriteBatchCoreAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct)
    {
        // Audit is part of the business transaction.
        // Autocommit would break atomicity and produce orphan events.
        await uow.EnsureOpenForTransactionAsync(ct);

        var count = auditEvents.Count;
        var auditEventIds = new Guid[count];
        var entityKinds = new short[count];
        var entityIds = new Guid[count];
        var actionCodes = new string[count];
        var actorUserIds = new Guid?[count];
        var occurredAtUts = new DateTime[count];
        var correlationIds = new Guid?[count];
        var metadataJsons = new string?[count];

        var totalChangeCount = 0;

        for (var i = 0; i < auditEvents.Count; i++)
        {
            var auditEvent = auditEvents[i];

            auditEventIds[i] = auditEvent.AuditEventId;
            entityKinds[i] = (short)auditEvent.EntityKind;
            entityIds[i] = auditEvent.EntityId;
            actionCodes[i] = auditEvent.ActionCode.Trim();
            actorUserIds[i] = auditEvent.ActorUserId;
            occurredAtUts[i] = auditEvent.OccurredAtUtc;
            correlationIds[i] = auditEvent.CorrelationId;
            metadataJsons[i] = string.IsNullOrWhiteSpace(auditEvent.MetadataJson)
                ? null
                : auditEvent.MetadataJson;
            totalChangeCount += auditEvent.Changes.Count;
        }

        const string insertEventsSql = """
                                      INSERT INTO platform_audit_events
                                      (audit_event_id, entity_kind, entity_id, action_code,
                                       actor_user_id, occurred_at_utc, correlation_id, metadata)
                                      SELECT
                                          x.audit_event_id,
                                          x.entity_kind,
                                          x.entity_id,
                                          x.action_code,
                                          x.actor_user_id,
                                          x.occurred_at_utc,
                                          x.correlation_id,
                                          CASE WHEN x.metadata_json IS NULL THEN NULL ELSE x.metadata_json::jsonb END
                                      FROM UNNEST(
                                          @AuditEventIds::uuid[],
                                          @EntityKinds::smallint[],
                                          @EntityIds::uuid[],
                                          @ActionCodes::text[],
                                          @ActorUserIds::uuid[],
                                          @OccurredAtUtc::timestamptz[],
                                          @CorrelationIds::uuid[],
                                          @MetadataJsons::text[]
                                      ) AS x(
                                          audit_event_id,
                                          entity_kind,
                                          entity_id,
                                          action_code,
                                          actor_user_id,
                                          occurred_at_utc,
                                          correlation_id,
                                          metadata_json
                                      );
                                      """;

        var insertEventCmd = new CommandDefinition(
            insertEventsSql,
            new
            {
                AuditEventIds = auditEventIds,
                EntityKinds = entityKinds,
                EntityIds = entityIds,
                ActionCodes = actionCodes,
                ActorUserIds = actorUserIds,
                OccurredAtUtc = occurredAtUts,
                CorrelationIds = correlationIds,
                MetadataJsons = metadataJsons
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(insertEventCmd);

        if (totalChangeCount == 0)
            return;

        // Insert changes in a single roundtrip.
        // ordinal is 1-based and preserves deterministic order for an event.
        var changeIds = new Guid[totalChangeCount];
        var eventIds = new Guid[totalChangeCount];
        var ordinals = new int[totalChangeCount];
        var fieldPaths = new string[totalChangeCount];
        var oldValues = new string?[totalChangeCount];
        var newValues = new string?[totalChangeCount];

        var changeIndex = 0;

        foreach (var auditEvent in auditEvents)
        {
            for (var ordinal = 0; ordinal < auditEvent.Changes.Count; ordinal++)
            {
                var c = auditEvent.Changes[ordinal];

                changeIds[changeIndex] = Guid.CreateVersion7();
                eventIds[changeIndex] = auditEvent.AuditEventId;
                ordinals[changeIndex] = ordinal + 1;
                fieldPaths[changeIndex] = c.FieldPath.Trim();
                oldValues[changeIndex] = string.IsNullOrWhiteSpace(c.OldValueJson) ? null : c.OldValueJson;
                newValues[changeIndex] = string.IsNullOrWhiteSpace(c.NewValueJson) ? null : c.NewValueJson;
                changeIndex++;
            }
        }

        const string insertChangesSql = """
                                       INSERT INTO platform_audit_event_changes
                                       (audit_change_id, audit_event_id, ordinal, field_path, old_value_jsonb, new_value_jsonb)
                                       SELECT
                                           c.audit_change_id,
                                           c.audit_event_id,
                                           c.ordinal,
                                           c.field_path,
                                           CASE WHEN c.old_value IS NULL THEN NULL ELSE c.old_value::jsonb END,
                                           CASE WHEN c.new_value IS NULL THEN NULL ELSE c.new_value::jsonb END
                                       FROM UNNEST(
                                           @ChangeIds::uuid[],
                                           @EventIds::uuid[],
                                           @Ordinals::int[],
                                           @FieldPaths::text[],
                                           @OldValues::text[],
                                           @NewValues::text[]
                                       ) AS c(audit_change_id, audit_event_id, ordinal, field_path, old_value, new_value);
                                       """;

        var insertChangesCmd = new CommandDefinition(
            insertChangesSql,
            new
            {
                ChangeIds = changeIds,
                EventIds = eventIds,
                Ordinals = ordinals,
                FieldPaths = fieldPaths,
                OldValues = oldValues,
                NewValues = newValues
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(insertChangesCmd);
    }

    private static AuditEvent NormalizeAuditEvent(AuditEvent auditEvent, string rootParamName)
    {
        if (auditEvent.AuditEventId == Guid.Empty)
            throw new NgbArgumentInvalidException(rootParamName, "AuditEventId must not be empty.");

        if (auditEvent.EntityId == Guid.Empty)
            throw new NgbArgumentInvalidException(rootParamName, "EntityId must not be empty.");

        if (string.IsNullOrWhiteSpace(auditEvent.ActionCode))
            throw new NgbArgumentInvalidException(rootParamName, "ActionCode must not be empty.");

        auditEvent.OccurredAtUtc.EnsureUtc(nameof(auditEvent.OccurredAtUtc));

        var normalizedMetadata = string.IsNullOrWhiteSpace(auditEvent.MetadataJson)
            ? null
            : auditEvent.MetadataJson;

        var changes = auditEvent.Changes ?? Array.Empty<AuditFieldChange>();
        if (changes.Count == 0)
        {
            return auditEvent with
            {
                ActionCode = auditEvent.ActionCode.Trim(),
                MetadataJson = normalizedMetadata,
                Changes = Array.Empty<AuditFieldChange>()
            };
        }

        var normalizedChanges = new AuditFieldChange[changes.Count];

        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];

            if (string.IsNullOrWhiteSpace(change.FieldPath))
                throw new NgbArgumentInvalidException(rootParamName, "FieldPath must not be empty.");

            normalizedChanges[i] = new AuditFieldChange(
                change.FieldPath.Trim(),
                string.IsNullOrWhiteSpace(change.OldValueJson) ? null : change.OldValueJson,
                string.IsNullOrWhiteSpace(change.NewValueJson) ? null : change.NewValueJson);
        }

        return auditEvent with
        {
            ActionCode = auditEvent.ActionCode.Trim(),
            MetadataJson = normalizedMetadata,
            Changes = normalizedChanges
        };
    }
}
