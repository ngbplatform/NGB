using System.Text.Json;
using Dapper;
using FluentAssertions;
using NGB.Core.AuditLog;
using Npgsql;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Performance;

[Collection(PostgresCollection.Name)]
public sealed class ExplainPlans_AuditLog_IndexUsage_P2Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AuditLog_EntityCursorQuery_UsesEntityPagingIndex()
    {
        // Ensure schema is bootstrapped.
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = DeterministicGuid.Create("audit|entity|explain");
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            // Target entity rows (the one we will query).
            const int targetCount = 500;
            var targetIds = new Guid[targetCount];
            var targetTimes = new DateTime[targetCount];
            var targetEntityIds = new Guid[targetCount];
            for (var i = 0; i < targetCount; i++)
            {
                targetIds[i] = DeterministicGuid.Create($"audit|event|target|{i}");
                targetTimes[i] = baseTime.AddMinutes(i);
                targetEntityIds[i] = entityId;
            }

            // Other entities: we intentionally insert many *newer* rows for other entities, so that a generic
            // (occurred_at_utc, audit_event_id) index would have to scan lots of non-matching rows before it finds
            // enough rows for the target entity. This makes the dedicated (entity_kind, entity_id, occurred_at_utc, audit_event_id)
            // index the rational choice.
            var otherEntityPool = new Guid[50];
            for (var i = 0; i < otherEntityPool.Length; i++)
                otherEntityPool[i] = DeterministicGuid.Create($"audit|entity|other|{i}");

            const int otherCount = 5000;
            var otherIds = new Guid[otherCount];
            var otherTimes = new DateTime[otherCount];
            var otherEntityIds = new Guid[otherCount];
            for (var i = 0; i < otherCount; i++)
            {
                otherIds[i] = DeterministicGuid.Create($"audit|event|other|{i}");
                // Minutes 600..1399 (10:00..23:19) are newer than all target rows (0..499 minutes).
                otherTimes[i] = baseTime.AddMinutes(600 + (i % 800));
                otherEntityIds[i] = otherEntityPool[i % otherEntityPool.Length];
            }

            const string insertSql = """
                                     INSERT INTO platform_audit_events
                                     (audit_event_id, entity_kind, entity_id, action_code, actor_user_id, occurred_at_utc, correlation_id, metadata)
                                     SELECT
                                         u.audit_event_id,
                                         @EntityKind,
                                         u.entity_id,
                                         @ActionCode,
                                         NULL,
                                         u.occurred_at_utc,
                                         NULL,
                                         NULL
                                     FROM UNNEST(@AuditEventIds::uuid[], @EntityIds::uuid[], @OccurredAtUtc::timestamptz[])
                                         AS u(audit_event_id, entity_id, occurred_at_utc);
                                     """;

            await conn.ExecuteAsync(
                insertSql,
                new
                {
                    AuditEventIds = otherIds,
                    EntityIds = otherEntityIds,
                    OccurredAtUtc = otherTimes,
                    EntityKind = (short)AuditEntityKind.Document,
                    ActionCode = "document.post"
                });

            await conn.ExecuteAsync(
                insertSql,
                new
                {
                    AuditEventIds = targetIds,
                    EntityIds = targetEntityIds,
                    OccurredAtUtc = targetTimes,
                    EntityKind = (short)AuditEntityKind.Document,
                    ActionCode = "document.post"
                });

            await conn.ExecuteAsync("ANALYZE platform_audit_events;");
        }

        var querySql = """
                       SELECT audit_event_id
                       FROM platform_audit_events
                       WHERE entity_kind = @EntityKind
                         AND entity_id = @EntityId
                         AND (occurred_at_utc, audit_event_id) < (@AfterOccurredAtUtc, @AfterAuditEventId)
                       ORDER BY occurred_at_utc DESC, audit_event_id DESC
                       LIMIT @Limit;
                       """;

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            querySql,
            new
            {
                EntityKind = (short)AuditEntityKind.Document,
                EntityId = entityId,
                AfterOccurredAtUtc = baseTime.AddDays(1),
                AfterAuditEventId = DeterministicGuid.Create("audit|cursor|max"),
                Limit = 50
            },
            disableSeqScan: true);

        PlanContainsIndex(plan, "ix_platform_audit_events_entity_occurred_at_id_desc")
            .Should()
            .BeTrue("audit log entity paging should use the dedicated cursor index");
    }

    private static async Task<JsonElement> ExplainJsonAsync(
        string cs,
        string sql,
        object? args,
        bool disableSeqScan)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        if (disableSeqScan)
            await conn.ExecuteAsync("SET enable_seqscan TO off;");

        await using var cmd = new NpgsqlCommand("EXPLAIN (FORMAT JSON) " + sql, conn);
        if (args is not null)
        {
            foreach (var p in args.GetType().GetProperties())
            {
                var value = p.GetValue(args);
                cmd.Parameters.AddWithValue(p.Name, value ?? DBNull.Value);
            }
        }

        await using var reader = await cmd.ExecuteReaderAsync(CancellationToken.None);
        await reader.ReadAsync(CancellationToken.None);
        var raw = reader.GetValue(0)?.ToString() ?? string.Empty;

        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement[0].GetProperty("Plan").Clone();
    }

    private static bool PlanContainsIndex(JsonElement plan, string indexName)
    {
        if (plan.ValueKind != JsonValueKind.Object)
            return false;

        if (plan.TryGetProperty("Index Name", out var idx) && idx.ValueKind == JsonValueKind.String)
        {
            if (string.Equals(idx.GetString(), indexName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (plan.TryGetProperty("Plans", out var sub) && sub.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in sub.EnumerateArray())
                if (PlanContainsIndex(child, indexName))
                    return true;
        }

        return false;
    }
}
