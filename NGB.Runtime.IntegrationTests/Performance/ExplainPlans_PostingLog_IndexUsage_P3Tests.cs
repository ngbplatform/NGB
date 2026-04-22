using System.Text.Json;
using Dapper;
using FluentAssertions;
using NGB.Accounting.PostingState;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Performance;

[Collection(PostgresCollection.Name)]
public sealed class ExplainPlans_PostingLog_IndexUsage_P3Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostingLog_DefaultPagingQuery_UsesDescendingPageOrderIndex()
    {
        await SeedAsync(Fixture.ConnectionString);

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT
                l.document_id,
                l.operation,
                l.started_at_utc,
                l.completed_at_utc
            FROM accounting_posting_state l
            WHERE l.started_at_utc >= @FromUtc::timestamptz
              AND l.started_at_utc <= @ToUtc::timestamptz
              AND (l.started_at_utc, l.document_id, l.operation) < (@AfterStarted::timestamptz, @AfterDoc::uuid, @AfterOp::smallint)
            ORDER BY l.started_at_utc DESC, l.document_id DESC, l.operation DESC
            LIMIT @Limit;
            """,
            new
            {
                FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ToUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                AfterStarted = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AfterDoc = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                AfterOp = short.MaxValue,
                Limit = 50
            },
            disableSeqScan: true,
            disableSort: true);

        PlanContainsNodeType(plan, "Seq Scan").Should().BeFalse("posting log paging must stay index-backed");
        PlanContainsNodeType(plan, "Sort").Should().BeFalse("posting log paging should stream rows in index order");
        PlanContainsIndex(plan, "ix_accounting_posting_state_page_order")
            .Should().BeTrue("default posting log paging should use the descending page-order index");
    }

    [Fact]
    public async Task PostingLog_OperationFilteredPagingQuery_UsesOperationSpecificPagingIndex()
    {
        await SeedAsync(Fixture.ConnectionString);

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT
                l.document_id,
                l.operation,
                l.started_at_utc,
                l.completed_at_utc
            FROM accounting_posting_state l
            WHERE l.started_at_utc >= @FromUtc::timestamptz
              AND l.started_at_utc <= @ToUtc::timestamptz
              AND l.operation = @Operation::smallint
              AND (l.started_at_utc, l.document_id, l.operation) < (@AfterStarted::timestamptz, @AfterDoc::uuid, @AfterOp::smallint)
            ORDER BY l.started_at_utc DESC, l.document_id DESC, l.operation DESC
            LIMIT @Limit;
            """,
            new
            {
                FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ToUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                Operation = (short)PostingOperation.Unpost,
                AfterStarted = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AfterDoc = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                AfterOp = (short)PostingOperation.Unpost,
                Limit = 50
            },
            disableSeqScan: true,
            disableSort: true);

        PlanContainsNodeType(plan, "Seq Scan").Should().BeFalse("operation-filtered posting log paging must stay index-backed");
        PlanContainsNodeType(plan, "Sort").Should().BeFalse("operation-filtered posting log paging should not add a runtime sort");
        PlanContainsIndex(plan, "ix_accounting_posting_state_operation_page_order")
            .Should().BeTrue("operation-filtered posting log paging should use the operation-specific index");
    }

    [Fact]
    public async Task PostingLog_StaleInProgressPagingQuery_UsesIncompletePartialPagingIndex()
    {
        await SeedAsync(Fixture.ConnectionString);

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            """
            SELECT
                l.document_id,
                l.operation,
                l.started_at_utc,
                l.completed_at_utc
            FROM accounting_posting_state l
            WHERE l.started_at_utc >= @FromUtc::timestamptz
              AND l.started_at_utc <= @ToUtc::timestamptz
              AND l.completed_at_utc IS NULL
              AND l.started_at_utc < @StaleCutoffUtc::timestamptz
              AND (l.started_at_utc, l.document_id, l.operation) < (@AfterStarted::timestamptz, @AfterDoc::uuid, @AfterOp::smallint)
            ORDER BY l.started_at_utc DESC, l.document_id DESC, l.operation DESC
            LIMIT @Limit;
            """,
            new
            {
                FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ToUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                StaleCutoffUtc = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                AfterStarted = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AfterDoc = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                AfterOp = short.MaxValue,
                Limit = 50
            },
            disableSeqScan: true,
            disableSort: true);

        PlanContainsNodeType(plan, "Seq Scan").Should().BeFalse("stale in-progress paging must stay index-backed");
        PlanContainsNodeType(plan, "Sort").Should().BeFalse("stale in-progress paging should stream rows from the partial index");
        PlanContainsIndex(plan, "ix_accounting_posting_state_incomplete_page_order")
            .Should().BeTrue("stale in-progress paging should use the incomplete-row partial index");
    }

    private static async Task SeedAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var oldCompletedCount = 6_000;
        var targetUnpostCount = 400;
        var staleInProgressCount = 500;

        var oldCompletedDocumentIds = new Guid[oldCompletedCount];
        var oldCompletedOperations = new short[oldCompletedCount];
        var oldCompletedStartedAtUtc = new DateTime[oldCompletedCount];
        var oldCompletedCompletedAtUtc = new DateTime?[oldCompletedCount];

        var baseCompleted = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < oldCompletedCount; i++)
        {
            oldCompletedDocumentIds[i] = Guid.CreateVersion7();
            oldCompletedOperations[i] = (short)PostingOperation.Post;
            oldCompletedStartedAtUtc[i] = baseCompleted.AddMinutes(i);
            oldCompletedCompletedAtUtc[i] = oldCompletedStartedAtUtc[i].AddSeconds(10);
        }

        var targetUnpostDocumentIds = new Guid[targetUnpostCount];
        var targetUnpostOperations = new short[targetUnpostCount];
        var targetUnpostStartedAtUtc = new DateTime[targetUnpostCount];
        var targetUnpostCompletedAtUtc = new DateTime?[targetUnpostCount];

        var baseUnpost = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < targetUnpostCount; i++)
        {
            targetUnpostDocumentIds[i] = Guid.CreateVersion7();
            targetUnpostOperations[i] = (short)PostingOperation.Unpost;
            targetUnpostStartedAtUtc[i] = baseUnpost.AddMinutes(i);
            targetUnpostCompletedAtUtc[i] = targetUnpostStartedAtUtc[i].AddSeconds(5);
        }

        var staleDocumentIds = new Guid[staleInProgressCount];
        var staleOperations = new short[staleInProgressCount];
        var staleStartedAtUtc = new DateTime[staleInProgressCount];
        var staleCompletedAtUtc = new DateTime?[staleInProgressCount];

        var baseStale = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < staleInProgressCount; i++)
        {
            staleDocumentIds[i] = Guid.CreateVersion7();
            staleOperations[i] = (short)PostingOperation.Post;
            staleStartedAtUtc[i] = baseStale.AddMinutes(i);
            staleCompletedAtUtc[i] = null;
        }

        const string insertSql = """
                                 INSERT INTO accounting_posting_state(document_id, operation, started_at_utc, completed_at_utc)
                                 SELECT u.document_id, u.operation, u.started_at_utc, u.completed_at_utc
                                 FROM UNNEST(@DocumentIds::uuid[], @Operations::smallint[], @StartedAtUtc::timestamptz[], @CompletedAtUtc::timestamptz[])
                                     AS u(document_id, operation, started_at_utc, completed_at_utc);
                                 """;

        await conn.ExecuteAsync(
            insertSql,
            new
            {
                DocumentIds = oldCompletedDocumentIds,
                Operations = oldCompletedOperations,
                StartedAtUtc = oldCompletedStartedAtUtc,
                CompletedAtUtc = oldCompletedCompletedAtUtc
            });

        await conn.ExecuteAsync(
            insertSql,
            new
            {
                DocumentIds = targetUnpostDocumentIds,
                Operations = targetUnpostOperations,
                StartedAtUtc = targetUnpostStartedAtUtc,
                CompletedAtUtc = targetUnpostCompletedAtUtc
            });

        await conn.ExecuteAsync(
            insertSql,
            new
            {
                DocumentIds = staleDocumentIds,
                Operations = staleOperations,
                StartedAtUtc = staleStartedAtUtc,
                CompletedAtUtc = staleCompletedAtUtc
            });

        await conn.ExecuteAsync("ANALYZE accounting_posting_state;");
    }

    private static async Task<JsonElement> ExplainJsonAsync(
        string connectionString,
        string sql,
        object args,
        bool disableSeqScan,
        bool disableSort)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        if (disableSeqScan)
            await conn.ExecuteAsync("SET enable_seqscan TO off;");

        if (disableSort)
            await conn.ExecuteAsync("SET enable_sort TO off;");

        await using var cmd = new NpgsqlCommand("EXPLAIN (FORMAT JSON) " + sql, conn);
        foreach (var property in args.GetType().GetProperties())
        {
            var value = property.GetValue(args);
            cmd.Parameters.AddWithValue(property.Name, value ?? DBNull.Value);
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

        if (plan.TryGetProperty("Index Name", out var index) && index.ValueKind == JsonValueKind.String)
        {
            if (string.Equals(index.GetString(), indexName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (plan.TryGetProperty("Plans", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                if (PlanContainsIndex(child, indexName))
                    return true;
            }
        }

        return false;
    }

    private static bool PlanContainsNodeType(JsonElement plan, string nodeType)
    {
        if (plan.ValueKind != JsonValueKind.Object)
            return false;

        if (plan.TryGetProperty("Node Type", out var currentNode) && currentNode.ValueKind == JsonValueKind.String)
        {
            if (string.Equals(currentNode.GetString(), nodeType, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (plan.TryGetProperty("Plans", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                if (PlanContainsNodeType(child, nodeType))
                    return true;
            }
        }

        return false;
    }
}
