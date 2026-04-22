using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterIndependentWriteService_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Upsert_IsIdempotent_ByCommandId_NoDuplicateRecords()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_UPSERT_IDEMP";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var (dims, dimSetId) = CreateBuildingKey();
        var cmd = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var r1 = await svc.UpsertAsync(
                registerId,
                dims,
                periodUtc: null,
                values: new Dictionary<string, object?> { ["amount"] = 10 },
                commandId: cmd,
                manageTransaction: true,
                ct: CancellationToken.None);

            var r2 = await svc.UpsertAsync(
                registerId,
                dims,
                periodUtc: null,
                values: new Dictionary<string, object?> { ["amount"] = 10 },
                commandId: cmd,
                manageTransaction: true,
                ct: CancellationToken.None);

            r1.Should().Be(ReferenceRegisterWriteResult.Executed);
            r2.Should().Be(ReferenceRegisterWriteResult.AlreadyCompleted);
        }

        var table = await ResolveRecordsTableAsync(host, registerId);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var count = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @DimSetId;",
                new { DimSetId = dimSetId });

            count.Should().Be(1, "idempotent upsert must not append duplicate versions");
        }
    }

    [Fact]
    public async Task Tombstone_CopiesValues_ForNotNullFields_AndHidesFromNonDeletedReads()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_TOMBSTONE_COPY";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var (dims, dimSetId) = CreateBuildingKey();

        // Arrange: insert an active record.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var res = await svc.UpsertAsync(
                registerId,
                dims,
                periodUtc: null,
                values: new Dictionary<string, object?> { ["amount"] = 42 },
                commandId: Guid.CreateVersion7(),
                manageTransaction: true,
                ct: CancellationToken.None);

            res.Should().Be(ReferenceRegisterWriteResult.Executed);
        }

        // Act: tombstone the key.
        var asOf = DateTime.UtcNow;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var res = await svc.TombstoneAsync(
                registerId,
                dims,
                asOfUtc: asOf,
                commandId: Guid.CreateVersion7(),
                manageTransaction: true,
                ct: CancellationToken.None);

            res.Should().Be(ReferenceRegisterWriteResult.Executed);
        }

        // Assert: latest version is a tombstone AND it copied values to satisfy NOT NULL columns.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();

            var deleted = await read.SliceLastByDimensionSetIdAsync(
                registerId,
                dimSetId,
                asOfUtc: DateTime.UtcNow.AddSeconds(1),
                recorderDocumentId: null,
                includeDeleted: true,
                ct: CancellationToken.None);

            deleted.Should().NotBeNull();
            deleted!.IsDeleted.Should().BeTrue();
            deleted.Values.Should().ContainKey("amount");
            deleted.Values["amount"].Should().Be(42);

            var visible = await read.SliceLastByDimensionSetIdAsync(
                registerId,
                dimSetId,
                asOfUtc: DateTime.UtcNow.AddSeconds(1),
                recorderDocumentId: null,
                includeDeleted: false,
                ct: CancellationToken.None);

            visible.Should().BeNull("tombstones must be hidden when includeDeleted=false");
        }

        var table = await ResolveRecordsTableAsync(host, registerId);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var versions = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @DimSetId;",
                new { DimSetId = dimSetId });

            versions.Should().Be(2, "tombstone must be represented as a new version appended to the append-only table");
        }
    }

    [Fact]
    public async Task Upsert_ConcurrentSameCommandId_SerializesByKeyLock_AndDoesNotDuplicate()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_UPSERT_CONC";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var (dims, dimSetId) = CreateBuildingKey();
        var cmd = Guid.CreateVersion7();

        const int workers = 8;

        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;

        async Task<ReferenceRegisterWriteResult> Worker()
        {
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            if (Interlocked.Increment(ref ready) == workers)
                allReady.TrySetResult();

            await allReady.Task;
            await go.Task;

            return await svc.UpsertAsync(
                registerId,
                dims,
                periodUtc: null,
                values: new Dictionary<string, object?> { ["amount"] = 7 },
                commandId: cmd,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        var tasks = Enumerable.Range(0, workers).Select(_ => Worker()).ToArray();

        await allReady.Task;
        go.TrySetResult();

        var results = await Task.WhenAll(tasks);

        results.Count(x => x == ReferenceRegisterWriteResult.Executed).Should().Be(1);
        results.Count(x => x == ReferenceRegisterWriteResult.AlreadyCompleted).Should().Be(workers - 1);

        var table = await ResolveRecordsTableAsync(host, registerId);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var count = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @DimSetId;",
                new { DimSetId = dimSetId });

            count.Should().Be(1, "key-level lock + command idempotency must prevent duplicates under concurrency");
        }
    }

    [Fact]
    public async Task Upsert_WhenAppendThrows_RollsBack_Log_And_DoesNotCommitAnything()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                // Add the concrete store and then override the interface with a decorator
                // that throws on AppendAsync but still delegates EnsureSchemaAsync.
                services.AddScoped<PostgresReferenceRegisterRecordsStore>();
                services.AddScoped<IReferenceRegisterRecordsStore>(sp =>
                    new ThrowingAppendReferenceRegisterRecordsStore(sp.GetRequiredService<PostgresReferenceRegisterRecordsStore>()));
            });

        const string code = "RR_IND_ROLLBACK";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var (dims, dimSetId) = CreateBuildingKey();
        var cmd = Guid.CreateVersion7();

        // Act: upsert fails.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            Func<Task> act = async () =>
                await svc.UpsertAsync(
                    registerId,
                    dims,
                    periodUtc: null,
                    values: new Dictionary<string, object?> { ["amount"] = 1 },
                    commandId: cmd,
                    manageTransaction: true,
                    ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*throwing store*");
        }

        // Assert: write state row is not committed (transaction rolled back).
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var logCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM reference_register_independent_write_state WHERE register_id = @RegisterId;",
                new { RegisterId = registerId });

            logCount.Should().Be(0);
        }

        // Assert: no record versions were appended.
        var table = await ResolveRecordsTableAsync(host, registerId);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var count = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @DimSetId;",
                new { DimSetId = dimSetId });

            count.Should().Be(0);
        }
    }

    [Fact]
    public async Task Upsert_Takeover_StaleInProgressRow_Succeeds()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_TAKEOVER";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var (dims, dimSetId) = CreateBuildingKey();
        var cmd = Guid.CreateVersion7();

        // Arrange: insert a stale in-progress row.
        var oldStartedAt = DateTime.UtcNow.AddMinutes(-30);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            await conn.ExecuteAsync(
                """
                INSERT INTO reference_register_independent_write_state(register_id, command_id, operation, started_at_utc, completed_at_utc)
                VALUES (@RegisterId, @CommandId, 1, @StartedAtUtc, NULL);
                """,
                new { RegisterId = registerId, CommandId = cmd, StartedAtUtc = oldStartedAt });
        }

        // Act: should take over and complete.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var res = await svc.UpsertAsync(
                registerId,
                dims,
                periodUtc: null,
                values: new Dictionary<string, object?> { ["amount"] = 99 },
                commandId: cmd,
                manageTransaction: true,
                ct: CancellationToken.None);

            res.Should().Be(ReferenceRegisterWriteResult.Executed);
        }

        // Assert: log row is completed and one record exists.
        var table = await ResolveRecordsTableAsync(host, registerId);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var completedAt = await conn.ExecuteScalarAsync<DateTime?>(
                """
                SELECT completed_at_utc
                FROM reference_register_independent_write_state
                WHERE register_id = @RegisterId AND command_id = @CommandId AND operation = 1;
                """,
                new { RegisterId = registerId, CommandId = cmd });

            completedAt.Should().NotBeNull();

            var count = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @DimSetId;",
                new { DimSetId = dimSetId });

            count.Should().Be(1);
        }
    }

    [Fact]
    public async Task Tombstone_Periodic_BackdatedAsOf_FindsRecord_And_AppendsTombstone()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_TOMBSTONE_PERIODIC_BACKDATED";
        var registerId = await ArrangeIndependentRegisterAsync(host, code, ReferenceRegisterPeriodicity.Day);

        var (dims, dimSetId) = CreateBuildingKey();

        var p1 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var p2 = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        // Arrange: insert a backdated active record.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var res = await svc.UpsertAsync(
                registerId,
                dims,
                periodUtc: p1,
                values: new Dictionary<string, object?> { ["amount"] = 42 },
                commandId: Guid.CreateVersion7(),
                manageTransaction: true,
                ct: CancellationToken.None);

            res.Should().Be(ReferenceRegisterWriteResult.Executed);
        }

        // Act: tombstone the key as-of an effective moment in the same timeline (also backdated).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var res = await svc.TombstoneAsync(
                registerId,
                dims,
                asOfUtc: p2,
                commandId: Guid.CreateVersion7(),
                manageTransaction: true,
                ct: CancellationToken.None);

            res.Should().Be(ReferenceRegisterWriteResult.Executed);
        }

        // Assert: latest version is a tombstone and it copied values.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();

            var deleted = await read.SliceLastByDimensionSetIdAsync(
                registerId,
                dimSetId,
                asOfUtc: DateTime.UtcNow,
                recorderDocumentId: null,
                includeDeleted: true,
                ct: CancellationToken.None);

            deleted.Should().NotBeNull();
            deleted!.IsDeleted.Should().BeTrue();
            deleted.Values["amount"].Should().Be(42);

            var visible = await read.SliceLastByDimensionSetIdAsync(
                registerId,
                dimSetId,
                asOfUtc: DateTime.UtcNow,
                recorderDocumentId: null,
                includeDeleted: false,
                ct: CancellationToken.None);

            visible.Should().BeNull();
        }

        var table = await ResolveRecordsTableAsync(host, registerId);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var versions = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @DimSetId;",
                new { DimSetId = dimSetId });

            versions.Should().Be(2, "tombstone must append a second version even for backdated as-of");
        }
    }

    private static (IReadOnlyList<DimensionValue> Dimensions, Guid DimensionSetId) CreateBuildingKey()
    {
        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var buildingValueId = DeterministicGuid.Create("Building|A");

        var dims = new[] { new DimensionValue(buildingDimId, buildingValueId) };
        var setId = DeterministicDimensionSetId.FromBag(new DimensionBag(dims));

        return (dims, setId);
    }

    private static async Task<Guid> ArrangeIndependentRegisterAsync(IHost host, string code, ReferenceRegisterPeriodicity periodicity = ReferenceRegisterPeriodicity.NonPeriodic)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var registerId = await mgmt.UpsertAsync(
            code,
            name: $"{code} name",
            periodicity: periodicity,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct: CancellationToken.None);

        await mgmt.ReplaceFieldsAsync(
            registerId,
            fields:
            [
                new ReferenceRegisterFieldDefinition(
                    Code: "amount",
                    Name: "Amount",
                    Ordinal: 10,
                    ColumnType: ColumnType.Int32,
                    IsNullable: false)
            ],
            ct: CancellationToken.None);

        var dimId = DeterministicGuid.Create("Dimension|building");

        await mgmt.ReplaceDimensionRulesAsync(
            registerId,
            rules:
            [
                new ReferenceRegisterDimensionRule(
                    DimensionId: dimId,
                    DimensionCode: "building",
                    Ordinal: 10,
                    IsRequired: true)
            ],
            ct: CancellationToken.None);

        return registerId;
    }

    private static async Task<string> ResolveRecordsTableAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();
        var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
        reg.Should().NotBeNull();

        return ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
    }

    private sealed class ThrowingAppendReferenceRegisterRecordsStore(PostgresReferenceRegisterRecordsStore inner)
        : IReferenceRegisterRecordsStore
    {
        public Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default) => inner.EnsureSchemaAsync(registerId, ct);

        public Task AppendAsync(Guid registerId, IReadOnlyList<ReferenceRegisterRecordWrite> records, CancellationToken ct = default)
            => throw new NotSupportedException("throwing store: simulated failure after write log begin");
    }
}
