using System.Text;
using Dapper;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.AuditLog;

public sealed class PostgresAuditEventReader(IUnitOfWork uow) : IAuditEventReader
{
    private sealed record EventRow(
        Guid AuditEventId,
        short EntityKind,
        Guid EntityId,
        string ActionCode,
        Guid? ActorUserId,
        DateTime OccurredAtUtc,
        Guid? CorrelationId,
        string? MetadataJson);

    private sealed record ChangeRow(
        Guid AuditEventId,
        int Ordinal,
        string FieldPath,
        string? OldValueJson,
        string? NewValueJson);

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        if (query is null)
            throw new NgbArgumentRequiredException(nameof(query));

        if (query.Limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(query.Limit), query.Limit, "Limit must be positive.");

        if (query.Offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(query.Offset), query.Offset, "Offset must be non-negative.");

        if (query.FromUtc.HasValue)
            query.FromUtc.Value.EnsureUtc(nameof(query.FromUtc));

        if (query.ToUtc.HasValue)
            query.ToUtc.Value.EnsureUtc(nameof(query.ToUtc));

        if (query.AfterOccurredAtUtc.HasValue)
            query.AfterOccurredAtUtc.Value.EnsureUtc(nameof(query.AfterOccurredAtUtc));

        var hasCursorTime = query.AfterOccurredAtUtc.HasValue;
        var hasCursorId = query.AfterAuditEventId.HasValue;
        
        if (hasCursorTime != hasCursorId)
            throw new NgbArgumentInvalidException(nameof(query),
                "Cursor-based paging requires both AfterOccurredAtUtc and AfterAuditEventId.");

        if (hasCursorTime && query.Offset != 0)
            throw new NgbArgumentInvalidException(nameof(query.Offset), "When using cursor-based paging, Offset must be 0.");

        await uow.EnsureConnectionOpenAsync(ct);

        var sql = new StringBuilder();
        sql.AppendLine("SELECT");
        sql.AppendLine("  audit_event_id AS AuditEventId,");
        sql.AppendLine("  entity_kind AS EntityKind,");
        sql.AppendLine("  entity_id AS EntityId,");
        sql.AppendLine("  action_code AS ActionCode,");
        sql.AppendLine("  actor_user_id AS ActorUserId,");
        sql.AppendLine("  occurred_at_utc AS OccurredAtUtc,");
        sql.AppendLine("  correlation_id AS CorrelationId,");
        sql.AppendLine("  metadata::text AS MetadataJson");
        sql.AppendLine("FROM platform_audit_events");
        sql.AppendLine("WHERE 1=1");

        var p = new DynamicParameters();

        if (query.EntityKind.HasValue)
        {
            sql.AppendLine("  AND entity_kind = @EntityKind");
            p.Add("EntityKind", (short)query.EntityKind.Value);
        }

        if (query.EntityId.HasValue)
        {
            sql.AppendLine("  AND entity_id = @EntityId");
            p.Add("EntityId", query.EntityId.Value);
        }

        if (query.ActorUserId.HasValue)
        {
            sql.AppendLine("  AND actor_user_id = @ActorUserId");
            p.Add("ActorUserId", query.ActorUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.ActionCode))
        {
            sql.AppendLine("  AND action_code = @ActionCode");
            p.Add("ActionCode", query.ActionCode.Trim());
        }

        if (query.FromUtc.HasValue)
        {
            sql.AppendLine("  AND occurred_at_utc >= @FromUtc");
            p.Add("FromUtc", query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            sql.AppendLine("  AND occurred_at_utc <= @ToUtc");
            p.Add("ToUtc", query.ToUtc.Value);
        }

        // Cursor paging. Works with stable ordering (time desc + tiebreaker on id).
        if (hasCursorTime)
        {
            sql.AppendLine("  AND (occurred_at_utc, audit_event_id) < (@AfterOccurredAtUtc, @AfterAuditEventId)");
            p.Add("AfterOccurredAtUtc", query.AfterOccurredAtUtc!.Value);
            p.Add("AfterAuditEventId", query.AfterAuditEventId!.Value);
        }

        // Stable ordering (time desc + tiebreaker).
        sql.AppendLine("ORDER BY occurred_at_utc DESC, audit_event_id DESC");
        sql.AppendLine("LIMIT @Limit OFFSET @Offset;");
        p.Add("Limit", query.Limit);
        p.Add("Offset", query.Offset);

        var cmd = new CommandDefinition(
            sql.ToString(),
            p,
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<EventRow>(cmd)).AsList();
        if (rows.Count == 0)
            return [];

        var ids = rows.Select(x => x.AuditEventId).ToArray();

        const string changesSql = """
                                 SELECT
                                     audit_event_id AS AuditEventId,
                                     ordinal AS Ordinal,
                                     field_path AS FieldPath,
                                     old_value_jsonb::text AS OldValueJson,
                                     new_value_jsonb::text AS NewValueJson
                                 FROM platform_audit_event_changes
                                 WHERE audit_event_id = ANY(@AuditEventIds)
                                 ORDER BY audit_event_id, ordinal;
                                 """;

        var changesCmd = new CommandDefinition(
            changesSql,
            new { AuditEventIds = ids },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var changeRows = (await uow.Connection.QueryAsync<ChangeRow>(changesCmd)).AsList();

        var byEvent = new Dictionary<Guid, List<AuditFieldChange>>(capacity: rows.Count);

        foreach (var r in rows)
        {
            byEvent[r.AuditEventId] = [];
        }

        foreach (var c in changeRows)
        {
            if (!byEvent.TryGetValue(c.AuditEventId, out var list))
                continue;

            // Ordering is guaranteed by SQL.
            list.Add(new AuditFieldChange(c.FieldPath, c.OldValueJson, c.NewValueJson));
        }

        var result = new List<AuditEvent>(rows.Count);
        foreach (var r in rows)
        {
            var changes = byEvent[r.AuditEventId];
            result.Add(new AuditEvent(
                r.AuditEventId,
                (AuditEntityKind)r.EntityKind,
                r.EntityId,
                r.ActionCode,
                r.ActorUserId,
                r.OccurredAtUtc,
                r.CorrelationId,
                r.MetadataJson,
                changes));
        }

        return result;
    }
}
