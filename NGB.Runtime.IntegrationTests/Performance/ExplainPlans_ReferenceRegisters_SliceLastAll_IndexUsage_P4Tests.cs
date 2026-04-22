using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Performance;

[Collection(PostgresCollection.Name)]
public sealed class ExplainPlans_ReferenceRegisters_SliceLastAll_IndexUsage_P4Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RefReg_RecordsReader_SliceLastAll_Monthly_UsesKeyV2Index()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid registerId;
        string table;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();

            registerId = await svc.UpsertAsync(
                code: "RR_PERF_SLICE_ALL",
                name: "RR Perf Slice All",
                periodicity: ReferenceRegisterPeriodicity.Month,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
            reg.Should().NotBeNull();
            table = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        }

        var dimSets = Enumerable.Range(0, 80)
            .Select(i => DeterministicGuid.Create($"refreg|dimset|all|{i}"))
            .ToArray();

        const int versionsPerKey = 25;

        var basePeriod = new DateTime(2025, 3, 2, 0, 0, 0, DateTimeKind.Utc);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_sets(dimension_set_id) SELECT x FROM UNNEST(@Ids::uuid[]) AS x(dimension_set_id) ON CONFLICT DO NOTHING;",
                new { Ids = dimSets });

            var total = dimSets.Length * versionsPerKey;

            var dimSetIds = new Guid[total];
            var periodUtc = new DateTime[total];
            var periodBucketUtc = new DateTime[total];
            var recordedAtUtc = new DateTime[total];
            var isDeleted = new bool[total];

            var idx = 0;
            for (var d = 0; d < dimSets.Length; d++)
            {
                for (var i = 0; i < versionsPerKey; i++)
                {
                    var p = basePeriod.AddMonths(i % 16).AddDays(d % 20);

                    dimSetIds[idx] = dimSets[d];
                    periodUtc[idx] = p;
                    periodBucketUtc[idx] = ReferenceRegisterPeriodBucket.ComputeUtc(p, ReferenceRegisterPeriodicity.Month)!.Value;
                    recordedAtUtc[idx] = p.AddHours(3).AddSeconds(i);
                    isDeleted[idx] = false;
                    idx++;
                }
            }

            var insertSql = $"""
INSERT INTO {table} (dimension_set_id, period_utc, period_bucket_utc, recorded_at_utc, is_deleted)
SELECT x.dimension_set_id, x.period_utc, x.period_bucket_utc, x.recorded_at_utc, x.is_deleted
FROM UNNEST(@DimensionSetIds::uuid[], @PeriodUtc::timestamptz[], @PeriodBucketUtc::timestamptz[], @RecordedAtUtc::timestamptz[], @IsDeleted::boolean[])
    AS x(dimension_set_id, period_utc, period_bucket_utc, recorded_at_utc, is_deleted);
""";

            await conn.ExecuteAsync(
                insertSql,
                new
                {
                    DimensionSetIds = dimSetIds,
                    PeriodUtc = periodUtc,
                    PeriodBucketUtc = periodBucketUtc,
                    RecordedAtUtc = recordedAtUtc,
                    IsDeleted = isDeleted
                });

            await conn.ExecuteAsync($"ANALYZE {table};");
        }

        // Act: emulate SliceLastAllAsync (Monthly + Independent) query shape.
        var asOfUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var bucketAsOfUtc = ReferenceRegisterPeriodBucket.ComputeUtc(asOfUtc, ReferenceRegisterPeriodicity.Month);

        var querySql = $"""
SELECT DISTINCT ON (t.dimension_set_id, t.recorder_document_id)
    t.record_id
FROM {table} t
WHERE
    t.recorded_at_utc <= @AsOfUtc
    AND t.recorder_document_id IS NULL
    AND t.dimension_set_id > @AfterDimensionSetId
    AND t.period_utc <= @AsOfUtc
    AND t.period_bucket_utc <= @BucketAsOfUtc
ORDER BY
    t.dimension_set_id,
    t.recorder_document_id,
    t.period_bucket_utc DESC,
    t.period_utc DESC,
    t.recorded_at_utc DESC,
    t.record_id DESC
LIMIT @Limit;
""";

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            querySql,
            new
            {
                AsOfUtc = asOfUtc,
                BucketAsOfUtc = bucketAsOfUtc,
                AfterDimensionSetId = Guid.Empty,
                Limit = 100
            },
            disableSeqScan: true,
            disableSort: true);

        var ixKeyV2 = await GetIndexByPrefixAsync(Fixture.ConnectionString, table, "ix_refreg_key_v2_");
        ixKeyV2.Should().NotBeNull("schema ensure must create ix_refreg_key_v2_* on {0}", table);

        PlanContainsNodeType(plan, "Seq Scan").Should().BeFalse("slice-last-all should not degrade to a sequential scan");
        PlanContainsIndexWithPrefix(plan, "ix_refreg_")
            .Should()
            .BeTrue("slice-last-all paging should use an ix_refreg_* index on {0}", table);
    }

    private static async Task<JsonElement> ExplainJsonAsync(
        string connectionString,
        string sql,
        object? args,
        bool disableSeqScan,
        bool disableSort)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        if (disableSeqScan)
        {
            await conn.ExecuteAsync("SET enable_seqscan = off;");
            await conn.ExecuteAsync("SET enable_bitmapscan = off;");
            await conn.ExecuteAsync("SET enable_tidscan = off;");
        }

        if (disableSort)
        {
            await conn.ExecuteAsync("SET enable_sort = off;");
            await conn.ExecuteAsync("SET enable_incremental_sort = off;");
        }

        var explainSql = $"EXPLAIN (FORMAT JSON) {sql}";
        var json = await conn.ExecuteScalarAsync<string?>(explainSql, args);

        using var doc = JsonDocument.Parse(json ?? throw new InvalidOperationException("EXPLAIN (FORMAT JSON) returned null."));
        return doc.RootElement[0].GetProperty("Plan").Clone();
    }

    private static async Task<string?> GetIndexByPrefixAsync(string connectionString, string table, string indexPrefix)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        // pg_indexes.tablename has no schema prefix.
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND tablename = @TableName AND indexname LIKE @Prefix || '%' LIMIT 1;",
            new { TableName = table, Prefix = indexPrefix });
    }

    private static bool PlanContainsNodeType(JsonElement plan, string nodeType)
    {
        if (plan.ValueKind != JsonValueKind.Object)
            return false;

        if (plan.TryGetProperty("Node Type", out var nt) && nt.ValueKind == JsonValueKind.String)
        {
            if (string.Equals(nt.GetString(), nodeType, StringComparison.Ordinal))
                return true;
        }

        if (plan.TryGetProperty("Plans", out var subPlans) && subPlans.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in subPlans.EnumerateArray())
            {
                if (PlanContainsNodeType(p, nodeType))
                    return true;
            }
        }

        return false;
    }

    private static bool PlanContainsIndexWithPrefix(JsonElement plan, string indexPrefix)
    {
        if (plan.ValueKind != JsonValueKind.Object)
            return false;

        if (plan.TryGetProperty("Index Name", out var ix) && ix.ValueKind == JsonValueKind.String)
        {
            var name = ix.GetString();
            if (name is not null && name.StartsWith(indexPrefix, StringComparison.Ordinal))
                return true;
        }

        if (plan.TryGetProperty("Plans", out var subPlans) && subPlans.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in subPlans.EnumerateArray())
            {
                if (PlanContainsIndexWithPrefix(p, indexPrefix))
                    return true;
            }
        }

        return false;
    }
}
