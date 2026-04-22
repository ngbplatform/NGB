using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Performance;

[Collection(PostgresCollection.Name)]
public sealed class ExplainPlans_OperationalRegisters_Movements_IndexUsage_P3Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OpReg_Movements_GetByMonth_UsesMonthDimMoveIndex_ForPaging()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc);

        string table;

        // Arrange: register (no resources) + ensure movements schema.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
            var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "OR_PERF", "OR Perf"), nowUtc, CancellationToken.None);

            await store.EnsureSchemaAsync(regId, CancellationToken.None);

            var reg = await regRepo.GetByIdAsync(regId, CancellationToken.None);
            reg.Should().NotBeNull();
            table = OperationalRegisterNaming.MovementsTable(reg!.TableCode);

            await uow.CommitAsync(CancellationToken.None);
        }

        var periodMonth = new DateOnly(2026, 1, 1);

        // Many other dimension sets for the same month (to make the month+dim index the rational choice).
        var otherDimSets = Enumerable.Range(0, 40)
            .Select(i => DeterministicGuid.Create($"opreg|dimset|other|{i}"))
            .ToArray();

        var targetDimSet = DeterministicGuid.Create("opreg|dimset|target");

        // Seed dimension sets (FK on movements table).
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_sets(dimension_set_id) SELECT x FROM UNNEST(@Ids::uuid[]) AS x(dimension_set_id) ON CONFLICT DO NOTHING;",
                new { Ids = otherDimSets.Append(targetDimSet).ToArray() });

            const int perOther = 300;
            const int targetCount = 400;

            var total = otherDimSets.Length * perOther + targetCount;

            var docIds = new Guid[total];
            var occurred = new DateTime[total];
            var dimSetIds = new Guid[total];
            var isStorno = new bool[total];

            var baseTime = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

            var idx = 0;
            for (var d = 0; d < otherDimSets.Length; d++)
            {
                for (var i = 0; i < perOther; i++)
                {
                    docIds[idx] = DeterministicGuid.Create($"opreg|doc|other|{d}|{i}");
                    occurred[idx] = baseTime.AddSeconds(i);
                    dimSetIds[idx] = otherDimSets[d];
                    isStorno[idx] = false;
                    idx++;
                }
            }

            for (var i = 0; i < targetCount; i++)
            {
                docIds[idx] = DeterministicGuid.Create($"opreg|doc|target|{i}");
                occurred[idx] = baseTime.AddMinutes(10).AddSeconds(i);
                dimSetIds[idx] = targetDimSet;
                isStorno[idx] = false;
                idx++;
            }

            var insertSql = $"""
INSERT INTO {table} (document_id, occurred_at_utc, dimension_set_id, is_storno)
SELECT x.document_id, x.occurred_at_utc, x.dimension_set_id, x.is_storno
FROM UNNEST(@DocumentIds::uuid[], @OccurredAtUtc::timestamptz[], @DimensionSetIds::uuid[], @IsStorno::boolean[])
    AS x(document_id, occurred_at_utc, dimension_set_id, is_storno);
""";

            await conn.ExecuteAsync(
                insertSql,
                new
                {
                    DocumentIds = docIds,
                    OccurredAtUtc = occurred,
                    DimensionSetIds = dimSetIds,
                    IsStorno = isStorno
                });

            await conn.ExecuteAsync($"ANALYZE {table};");
        }

        // Act: emulate the core reader query shape (month + dim + paging by movement_id + order).
        var querySql = $"""
SELECT movement_id
FROM {table}
WHERE period_month = @PeriodMonth
  AND dimension_set_id = @DimensionSetId
  AND movement_id > @AfterMovementId
ORDER BY movement_id
LIMIT @Limit;
""";

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            querySql,
            new
            {
                PeriodMonth = periodMonth,
                DimensionSetId = targetDimSet,
                AfterMovementId = 0L,
                Limit = 50
            },
            disableSeqScan: true);

        PlanContainsIndex(plan, Ix(table, "month_dim_move"))
            .Should()
            .BeTrue("month+dimension paging should use (period_month, dimension_set_id, movement_id) index");
    }

    [Fact]
    public async Task OpReg_Movements_DistinctMonthsByDocument_UsesDocMonthNoStornoIndex()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var regId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc);

        string table;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
            var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "OR_PERF2", "OR Perf 2"), nowUtc, CancellationToken.None);

            await store.EnsureSchemaAsync(regId, CancellationToken.None);

            var reg = await regRepo.GetByIdAsync(regId, CancellationToken.None);
            reg.Should().NotBeNull();
            table = OperationalRegisterNaming.MovementsTable(reg!.TableCode);

            await uow.CommitAsync(CancellationToken.None);
        }

        var docId = DeterministicGuid.Create("opreg|doc|months");
        var dimSetId = Guid.Empty; // empty set exists by default

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            // 24 months of movements for a single document + storno duplicates.
            const int months = 24;
            var total = months * 2;

            var docIds = new Guid[total];
            var occurred = new DateTime[total];
            var dimSetIds = new Guid[total];
            var isStorno = new bool[total];

            for (var i = 0; i < months; i++)
            {
                var t = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc).AddMonths(i);

                docIds[i] = docId;
                occurred[i] = t;
                dimSetIds[i] = dimSetId;
                isStorno[i] = false;

                docIds[i + months] = docId;
                occurred[i + months] = t;
                dimSetIds[i + months] = dimSetId;
                isStorno[i + months] = true;
            }

            var insertSql = $"""
INSERT INTO {table} (document_id, occurred_at_utc, dimension_set_id, is_storno)
SELECT x.document_id, x.occurred_at_utc, x.dimension_set_id, x.is_storno
FROM UNNEST(@DocumentIds::uuid[], @OccurredAtUtc::timestamptz[], @DimensionSetIds::uuid[], @IsStorno::boolean[])
    AS x(document_id, occurred_at_utc, dimension_set_id, is_storno);
""";

            await conn.ExecuteAsync(
                insertSql,
                new
                {
                    DocumentIds = docIds,
                    OccurredAtUtc = occurred,
                    DimensionSetIds = dimSetIds,
                    IsStorno = isStorno
                });

            await conn.ExecuteAsync($"ANALYZE {table};");
        }

        var querySql = $"""
SELECT DISTINCT period_month
FROM {table}
WHERE document_id = @DocumentId
  AND is_storno = FALSE
ORDER BY period_month;
""";

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            querySql,
            new { DocumentId = docId },
            disableSeqScan: true);

        PlanContainsIndex(plan, Ix(table, "doc_month_nostorno"))
            .Should()
            .BeTrue("distinct months by document should use partial (document_id, period_month) WHERE is_storno=false index");
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

    private static string Ix(string table, string purpose)
        => "ix_opreg_" + purpose + "_" + Hash8(table + "|" + purpose);

    private static string Hash8(string s)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant()[..8];
}
