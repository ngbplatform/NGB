using System.Text.Json;
using Dapper;
using FluentAssertions;
using Npgsql;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Performance;

[Collection(PostgresCollection.Name)]
public sealed class ExplainPlans_DocumentRelationships_EdgePages_IndexUsage_P4Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DocumentRelationships_EdgePage_Outgoing_WithCursorAndCodeFilter_UsesFromIndexes()
    {
        // Ensure schema is bootstrapped.
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var docIds = Enumerable.Range(0, 120)
            .Select(i => DeterministicGuid.Create($"docrel|p4_page|doc|{i}"))
            .ToArray();

        var fromId = docIds[0];

        const int totalEdges = 2500;
        var relationshipIds = new Guid[totalEdges];
        var fromIds = new Guid[totalEdges];
        var toIds = new Guid[totalEdges];
        var codes = new string[totalEdges];
        var createdAtUtc = new DateTime[totalEdges];

        for (var i = 0; i < totalEdges; i++)
        {
            relationshipIds[i] = DeterministicGuid.Create($"docrel|p4_page|edge|{i}");

            // IMPORTANT: document_relationships has a unique triplet constraint and additional
            // cardinality uniqueness for some relationship codes. For stable explain-plan tests
            // we use a permissive code and ensure (from,to,code) is always unique by making `to` unique.
            var from = (i < 1200) ? fromId : docIds[1 + (i % (docIds.Length - 1))];
            var to = DeterministicGuid.Create($"docrel|p4_page|to|{i}");
            if (to == from)
                to = DeterministicGuid.Create($"docrel|p4_page|to|{i}|alt");

            fromIds[i] = from;
            toIds[i] = to;
            codes[i] = "related_to";
            createdAtUtc[i] = baseTime.AddSeconds(i);
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var allDocIds = docIds
                .Concat(fromIds)
                .Concat(toIds)
                .Distinct()
                .ToArray();

            await conn.ExecuteAsync(
                "INSERT INTO documents (id, type_code, date_utc, status) " +
                "SELECT x, 'it_docrel_page', @DateUtc, 1 FROM UNNEST(@Ids::uuid[]) AS x(id) " +
                "ON CONFLICT DO NOTHING;",
                new { Ids = allDocIds, DateUtc = baseTime });

            const string insertEdges = """
                                     INSERT INTO document_relationships
                                         (relationship_id, from_document_id, to_document_id, relationship_code, created_at_utc)
                                     SELECT
                                         u.relationship_id,
                                         u.from_document_id,
                                         u.to_document_id,
                                         u.relationship_code,
                                         u.created_at_utc
                                     FROM UNNEST(
                                         @RelationshipIds::uuid[],
                                         @FromIds::uuid[],
                                         @ToIds::uuid[],
                                         @RelationshipCodes::text[],
                                         @CreatedAtUtc::timestamptz[]
                                     ) AS u(relationship_id, from_document_id, to_document_id, relationship_code, created_at_utc);
                                     """;

            await conn.ExecuteAsync(
                insertEdges,
                new
                {
                    RelationshipIds = relationshipIds,
                    FromIds = fromIds,
                    ToIds = toIds,
                    RelationshipCodes = codes,
                    CreatedAtUtc = createdAtUtc
                });

            await conn.ExecuteAsync("ANALYZE documents;");
            await conn.ExecuteAsync("ANALYZE document_relationships;");
        }

        // Emulate PostgresDocumentRelationshipGraphReader.GetOutgoingPageAsync query shape.
        var afterCreated = baseTime.AddDays(1);
        var afterId = DeterministicGuid.Create("docrel|page|cursor|max");

        var querySql = """
                       SELECT r.relationship_id
                       FROM document_relationships r
                       JOIN documents d ON d.id = r.to_document_id
                       WHERE
                           r.from_document_id = @DocumentId::uuid
                           AND (@CodeNorm::text IS NULL OR r.relationship_code_norm = @CodeNorm::text)
                           AND (
                               @AfterCreated::timestamptz IS NULL
                               OR r.created_at_utc < @AfterCreated::timestamptz
                               OR (r.created_at_utc = @AfterCreated::timestamptz AND r.relationship_id < @AfterId::uuid)
                           )
                       ORDER BY r.created_at_utc DESC, r.relationship_id DESC
                       LIMIT @Limit;
                       """;

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            querySql,
            new
            {
                DocumentId = fromId,
                CodeNorm = "related_to",
                AfterCreated = afterCreated,
                AfterId = afterId,
                Limit = 100
            },
            disableSeqScan: true,
            disableSort: true);

        PlanContainsNodeType(plan, "Seq Scan").Should().BeFalse("edge paging must not degrade to sequential scan");
        PlanContainsIndexWithPrefix(plan, "ix_docrel_from_")
            .Should()
            .BeTrue("edge paging must use a document_relationships from_* index");
    }

    private static async Task<JsonElement> ExplainJsonAsync(
        string cs,
        string sql,
        object? args,
        bool disableSeqScan,
        bool disableSort)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        if (disableSeqScan)
            await conn.ExecuteAsync("SET enable_seqscan TO off;");

        if (disableSort)
        {
            await conn.ExecuteAsync("SET enable_sort TO off;");
            await conn.ExecuteAsync("SET enable_incremental_sort TO off;");
        }

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

    private static bool PlanContainsIndexWithPrefix(JsonElement plan, string prefix)
    {
        if (plan.ValueKind != JsonValueKind.Object)
            return false;

        if (plan.TryGetProperty("Index Name", out var idx) && idx.ValueKind == JsonValueKind.String)
        {
            var name = idx.GetString();
            if (!string.IsNullOrWhiteSpace(name) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (plan.TryGetProperty("Plans", out var sub) && sub.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in sub.EnumerateArray())
                if (PlanContainsIndexWithPrefix(child, prefix))
                    return true;
        }

        return false;
    }

    private static bool PlanContainsNodeType(JsonElement plan, string nodeType)
    {
        if (plan.ValueKind != JsonValueKind.Object)
            return false;

        if (plan.TryGetProperty("Node Type", out var nt) && nt.ValueKind == JsonValueKind.String)
        {
            if (string.Equals(nt.GetString(), nodeType, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (plan.TryGetProperty("Plans", out var sub) && sub.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in sub.EnumerateArray())
                if (PlanContainsNodeType(child, nodeType))
                    return true;
        }

        return false;
    }
}
