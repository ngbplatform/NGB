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
public sealed class ExplainPlans_ReferenceRegisters_IndexUsage_P3Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RefReg_RecordsReader_SliceLast_Monthly_UsesKeyV2Index()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid registerId;
        string table;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();

            registerId = await svc.UpsertAsync(
                code: "RR_PERF_SLICE",
                name: "RR Perf Slice",
                periodicity: ReferenceRegisterPeriodicity.Month,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
            reg.Should().NotBeNull();
            table = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        }

        // Arrange:
        // Use Monthly periodicity to make the v2 key index the only viable way to satisfy the ORDER BY
        // (it includes period_bucket_utc DESC, period_utc DESC, recorded_at_utc DESC, record_id DESC),
        // while the legacy (dimension_set_id, period_bucket_utc, recorded_at_utc DESC, record_id DESC) cannot.
        var otherDimSets = Enumerable.Range(0, 30)
            .Select(i => DeterministicGuid.Create($"refreg|dimset|other|{i}"))
            .ToArray();

        var targetDimSet = DeterministicGuid.Create("refreg|dimset|target");

        const int perOther = 120;   // per dimset
        const int targetCount = 400;

        var basePeriod = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_sets(dimension_set_id) SELECT x FROM UNNEST(@Ids::uuid[]) AS x(dimension_set_id) ON CONFLICT DO NOTHING;",
                new { Ids = otherDimSets.Append(targetDimSet).ToArray() });

            var total = otherDimSets.Length * perOther + targetCount;

            var dimSetIds = new Guid[total];
            var periodUtc = new DateTime[total];
            var periodBucketUtc = new DateTime[total];
            var recordedAtUtc = new DateTime[total];
            var isDeleted = new bool[total];

            var idx = 0;

            // Many keys, across many months.
            for (var d = 0; d < otherDimSets.Length; d++)
            {
                for (var i = 0; i < perOther; i++)
                {
                    var p = basePeriod.AddMonths(i % 18).AddDays(i % 25);

                    dimSetIds[idx] = otherDimSets[d];
                    periodUtc[idx] = p;
                    periodBucketUtc[idx] = ReferenceRegisterPeriodBucket.ComputeUtc(p, ReferenceRegisterPeriodicity.Month)!.Value;
                    recordedAtUtc[idx] = p.AddHours(1).AddSeconds(i);
                    isDeleted[idx] = false;
                    idx++;
                }
            }

            // Target key: dense history across months.
            for (var i = 0; i < targetCount; i++)
            {
                var p = basePeriod.AddMonths(i % 18).AddDays(5);
                dimSetIds[idx] = targetDimSet;
                periodUtc[idx] = p;
                periodBucketUtc[idx] = ReferenceRegisterPeriodBucket.ComputeUtc(p, ReferenceRegisterPeriodicity.Month)!.Value;
                recordedAtUtc[idx] = p.AddHours(2).AddSeconds(i);
                isDeleted[idx] = false;
                idx++;
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

        // Act: emulate SliceLastAsync (Monthly + Independent) query shape.
        var asOfUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var bucketAsOfUtc = ReferenceRegisterPeriodBucket.ComputeUtc(asOfUtc, ReferenceRegisterPeriodicity.Month);

        var querySql = $"""
SELECT record_id
FROM {table} t
WHERE t.dimension_set_id = @DimensionSetId
  AND t.recorder_document_id IS NULL
  AND t.recorded_at_utc <= @AsOfUtc
  AND t.period_utc <= @AsOfUtc
  AND t.period_bucket_utc <= @BucketAsOfUtc
ORDER BY t.period_bucket_utc DESC, t.period_utc DESC, t.recorded_at_utc DESC, t.record_id DESC
LIMIT 1;
""";

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            querySql,
            new
            {
                DimensionSetId = targetDimSet,
                AsOfUtc = asOfUtc,
                BucketAsOfUtc = bucketAsOfUtc
            },
            disableSeqScan: true,
            disableSort: true);

        var ixKeyV2 = await GetIndexByPrefixAsync(Fixture.ConnectionString, table, "ix_refreg_key_v2_");
        ixKeyV2.Should().NotBeNull("schema ensure must create ix_refreg_key_v2_* on {0}", table);

        PlanContainsNodeType(plan, "Seq Scan").Should().BeFalse("slice-last by key should not degrade to a sequential scan");
        PlanContainsIndexWithPrefix(plan, "ix_refreg_")
            .Should()
            .BeTrue("slice-last by key should use an ix_refreg_* index on {0}", table);
    }

    [Fact]
    public async Task RefReg_RecordsStore_AppendTombstones_SelectLastRows_UsesRecorderKeyV2Index()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid registerId;
        string table;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();

            registerId = await svc.UpsertAsync(
                code: "RR_PERF_TOMBSTONE",
                name: "RR Perf Tombstone",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
                ct: CancellationToken.None);

            var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
            reg.Should().NotBeNull();
            table = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        }

        var otherDimSets = Enumerable.Range(0, 20)
            .Select(i => DeterministicGuid.Create($"refreg|dimset|other|{i}"))
            .ToArray();

        var targetDimSet = DeterministicGuid.Create("refreg|dimset|target");

        var otherRecorders = Enumerable.Range(0, 10)
            .Select(i => DeterministicGuid.Create($"refreg|recorder|other|{i}"))
            .ToArray();

        var targetRecorder = DeterministicGuid.Create("refreg|recorder|target");

        const int perOtherRecorder = 250;
        const int targetCount = 450;

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            // recorder_document_id has an FK to documents(id) — insert minimal draft documents for recorders.
            await conn.ExecuteAsync(
                "INSERT INTO documents (id, type_code, date_utc, status) " +
                "SELECT x, 'it_refreg_perf', @DateUtc, 1 FROM UNNEST(@DocIds::uuid[]) AS x(id) " +
                "ON CONFLICT DO NOTHING;",
                new
                {
                    DocIds = otherRecorders.Append(targetRecorder).ToArray(),
                    DateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });

            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_sets(dimension_set_id) SELECT x FROM UNNEST(@Ids::uuid[]) AS x(dimension_set_id) ON CONFLICT DO NOTHING;",
                new { Ids = otherDimSets.Append(targetDimSet).ToArray() });

            var total = otherRecorders.Length * perOtherRecorder + targetCount;

            var dimSetIds = new Guid[total];
            var recorderIds = new Guid[total];
            var recordedAtUtc = new DateTime[total];
            var isDeleted = new bool[total];

            var baseTime = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

            var idx = 0;
            for (var r = 0; r < otherRecorders.Length; r++)
            {
                for (var i = 0; i < perOtherRecorder; i++)
                {
                    dimSetIds[idx] = otherDimSets[i % otherDimSets.Length];
                    recorderIds[idx] = otherRecorders[r];
                    recordedAtUtc[idx] = baseTime.AddSeconds(i);
                    isDeleted[idx] = false;
                    idx++;
                }
            }

            for (var i = 0; i < targetCount; i++)
            {
                dimSetIds[idx] = i % 2 == 0 ? targetDimSet : otherDimSets[i % otherDimSets.Length];
                recorderIds[idx] = targetRecorder;
                recordedAtUtc[idx] = baseTime.AddHours(1).AddSeconds(i);
                isDeleted[idx] = false;
                idx++;
            }

            var insertSql = $"""
INSERT INTO {table} (dimension_set_id, recorder_document_id, recorded_at_utc, is_deleted)
SELECT x.dimension_set_id, x.recorder_document_id, x.recorded_at_utc, x.is_deleted
FROM UNNEST(@DimensionSetIds::uuid[], @RecorderIds::uuid[], @RecordedAtUtc::timestamptz[], @IsDeleted::boolean[])
    AS x(dimension_set_id, recorder_document_id, recorded_at_utc, is_deleted);
""";

            await conn.ExecuteAsync(
                insertSql,
                new
                {
                    DimensionSetIds = dimSetIds,
                    RecorderIds = recorderIds,
                    RecordedAtUtc = recordedAtUtc,
                    IsDeleted = isDeleted
                });

            await conn.ExecuteAsync($"ANALYZE {table};");
        }

        // Act: emulate AppendTombstonesForRecorderAsync selection phase:
        // DISTINCT ON (dimension_set_id, recorder_document_id) ORDER BY dimension_set_id, recorder_document_id, recorded_at DESC, record_id DESC.
        var querySql = $"""
WITH last_rows AS (
    SELECT DISTINCT ON (t.dimension_set_id, t.recorder_document_id)
        t.dimension_set_id,
        t.recorder_document_id,
        t.is_deleted
    FROM {table} t
    WHERE t.recorder_document_id = @RecorderDocumentId
    ORDER BY t.dimension_set_id, t.recorder_document_id, t.recorded_at_utc DESC, t.record_id DESC
)
SELECT count(*)
FROM last_rows
WHERE is_deleted = FALSE;
""";

        var plan = await ExplainJsonAsync(
            Fixture.ConnectionString,
            querySql,
            new { RecorderDocumentId = targetRecorder },
            disableSeqScan: true,
            disableSort: true);

        var ixRecorderKeyV2 = await GetIndexByPrefixAsync(Fixture.ConnectionString, table, "ix_refreg_recorder_key_v2_");
        ixRecorderKeyV2.Should().NotBeNull("schema ensure must create ix_refreg_recorder_key_v2_* on {0}", table);

        PlanContainsNodeType(plan, "Seq Scan").Should().BeFalse("tombstone selection should not degrade to a sequential scan");
        PlanContainsIndexWithPrefix(plan, "ix_refreg_recorder_")
            .Should()
            .BeTrue("tombstone last-rows selection by recorder should use an ix_refreg_recorder_* index on {0}", table);
    }

    private static JsonElement ExplainJsonAsyncResult(JsonDocument doc)
        => doc.RootElement[0].GetProperty("Plan");

    private static async Task<JsonElement> ExplainJsonAsync(
        string connectionString,
        string sql,
        object? args,
        bool disableSeqScan,
        bool disableSort = false)
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
        return ExplainJsonAsyncResult(doc).Clone();
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
