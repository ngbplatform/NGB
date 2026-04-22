using System.Text.Json;
using Dapper;
using FluentAssertions;
using Npgsql;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Performance;

[Collection(PostgresCollection.Name)]
public sealed class ExplainPlans_DocumentRelationships_IndexUsage_P3Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DocumentRelationships_GraphReader_QueryEdgesByFromIds_UsesFromIndexes()
    {
        // Ensure schema is bootstrapped.
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var docIds = Enumerable.Range(0, 250)
            .Select(i => DeterministicGuid.Create($"docrel|p3_from|doc|{i}"))
            .ToArray();

        var targetFromId = docIds[0];

        const int totalEdges = 6000;
        var relationshipIds = new Guid[totalEdges];
        var fromIds = new Guid[totalEdges];
        var toIds = new Guid[totalEdges];
        var codes = new string[totalEdges];
        var createdAtUtc = new DateTime[totalEdges];

        for (var i = 0; i < totalEdges; i++)
        {
            relationshipIds[i] = DeterministicGuid.Create($"docrel|p3_from|edge|{i}");

            // IMPORTANT: document_relationships has uniqueness constraints that depend on relationship_code
            // (e.g., cardinality rules for created_from), plus a unique triplet constraint.
            // For index-usage tests we only need volume and a stable query shape, not business semantics.
            // So we use a single permissive code and ensure (from,to,code) is always unique.
            var from = i < 1200 ? targetFromId : docIds[1 + (i % (docIds.Length - 1))];
            var to = DeterministicGuid.Create($"docrel|p3_from|to|{i}");
            if (to == from)
                to = DeterministicGuid.Create($"docrel|p3_from|to|{i}|alt");

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
                "SELECT x, 'it_docrel_perf', @DateUtc, 1 FROM UNNEST(@Ids::uuid[]) AS x(id) " +
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

        // Emulate PostgresDocumentRelationshipGraphReader.QueryEdgesByFromIdsAsync
        var querySql = """
                       SELECT relationship_id
                       FROM document_relationships r
                       WHERE r.from_document_id = ANY(@FromIds)
                         AND (@CodeNorms::text[] IS NULL OR r.relationship_code_norm = ANY(@CodeNorms))
                       ORDER BY r.created_at_utc DESC, r.relationship_id DESC
                       LIMIT @Limit;
                       """;

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            querySql,
            new
            {
                FromIds = new[] { targetFromId },
                CodeNorms = (string[]?)null,
                Limit = 250
            },
            disableSeqScan: true,
            disableSort: true);

        PlanContainsNodeType(plan, "Seq Scan").Should().BeFalse("graph edge lookup should not degrade to sequential scan");
        PlanContainsIndexWithPrefix(plan, "ix_docrel_from_")
            .Should()
            .BeTrue("graph edge lookup must use a document_relationships from_* index");
    }

    [Fact]
    public async Task DocumentRelationships_GraphReader_QueryEdgesByToIds_UsesToIndexes()
    {
        // Ensure schema is bootstrapped.
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var docIds = Enumerable.Range(0, 220)
            .Select(i => DeterministicGuid.Create($"docrel|p3_to|doc|{i}"))
            .ToArray();

        var targetToId = docIds[0];

        const int totalEdges = 4500;
        var relationshipIds = new Guid[totalEdges];
        var fromIds = new Guid[totalEdges];
        var toIds = new Guid[totalEdges];
        var codes = new string[totalEdges];
        var createdAtUtc = new DateTime[totalEdges];

        for (var i = 0; i < totalEdges; i++)
        {
            relationshipIds[i] = DeterministicGuid.Create($"docrel|p3_to|edge|{i}");

            // See comments in the FromIds test: we avoid relationship codes with cardinality uniqueness
            // and ensure (from,to,code) is always unique.
            var to = i < 900 ? targetToId : DeterministicGuid.Create($"docrel|p3_to|to|{i}");
            var from = DeterministicGuid.Create($"docrel|p3_to|from|{i}");
            if (from == to)
                from = DeterministicGuid.Create($"docrel|p3_to|from|{i}|alt");

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
                "SELECT x, 'it_docrel_perf2', @DateUtc, 1 FROM UNNEST(@Ids::uuid[]) AS x(id) " +
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

        // Emulate PostgresDocumentRelationshipGraphReader.QueryEdgesByToIdsAsync
        var querySql = """
                       SELECT relationship_id
                       FROM document_relationships r
                       WHERE r.to_document_id = ANY(@ToIds)
                         AND (@CodeNorms::text[] IS NULL OR r.relationship_code_norm = ANY(@CodeNorms))
                       ORDER BY r.created_at_utc DESC, r.relationship_id DESC
                       LIMIT @Limit;
                       """;

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            querySql,
            new
            {
                ToIds = new[] { targetToId },
                CodeNorms = (string[]?)null,
                Limit = 200
            },
            disableSeqScan: true,
            disableSort: true);

        PlanContainsNodeType(plan, "Seq Scan").Should().BeFalse("graph edge lookup should not degrade to sequential scan");
        PlanContainsIndexWithPrefix(plan, "ix_docrel_to_")
            .Should()
            .BeTrue("graph edge lookup must use a document_relationships to_* index");
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
