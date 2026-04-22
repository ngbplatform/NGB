using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterIndependentWriteService_Atomicity_Rollback_WhenAppendThrows_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Upsert_WhenRecordsStoreThrows_RollsBack_LogAndAudit_AndIsRetryableWithSameCommandId()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_ATOMICITY_UPSERT_STORE_THROW";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var (dims, dimSetId) = CreateBuildingKey();
        var cmd = Guid.CreateVersion7();

        var baselineAuditEvents = await CountAuditEventsAsync(Fixture.ConnectionString);

        using var failingHost = CreateHostWithThrowingStore(Fixture.ConnectionString);

        await using (var scope = failingHost.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var act = () => svc.UpsertAsync(
                registerId,
                dims,
                periodUtc: null,
                values: new Dictionary<string, object?> { ["amount"] = 1 },
                commandId: cmd,
                manageTransaction: true,
                ct: CancellationToken.None);

            await act.Should()
                .ThrowAsync<NotSupportedException>()
                .WithMessage("*BOOM: records store append*");
        }

        // rollback => no audit, no log row
        (await CountAuditEventsAsync(Fixture.ConnectionString)).Should().Be(baselineAuditEvents);

        (await CountIndependentWriteLogRowsAsync(
                Fixture.ConnectionString,
                registerId,
                cmd,
                ReferenceRegisterIndependentWriteOperation.Upsert))
            .Should()
            .Be(0);

        // retry with healthy host, same commandId must succeed
        using var healthyHost = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using (var scope = healthyHost.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var res = await svc.UpsertAsync(
                registerId,
                dims,
                periodUtc: null,
                values: new Dictionary<string, object?> { ["amount"] = 1 },
                commandId: cmd,
                manageTransaction: true,
                ct: CancellationToken.None);

            res.Should().Be(ReferenceRegisterWriteResult.Executed);
        }

        (await CountAuditEventsAsync(Fixture.ConnectionString)).Should().Be(baselineAuditEvents + 1);

        (await CountIndependentWriteLogRowsAsync(
                Fixture.ConnectionString,
                registerId,
                cmd,
                ReferenceRegisterIndependentWriteOperation.Upsert))
            .Should()
            .Be(1);

        (await IsIndependentWriteLogCompletedAsync(
                Fixture.ConnectionString,
                registerId,
                cmd,
                ReferenceRegisterIndependentWriteOperation.Upsert))
            .Should()
            .BeTrue();

        var table = await ResolveRecordsTableAsync(healthyHost, registerId);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var versions = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @DimSetId;",
                new { DimSetId = dimSetId });

            versions.Should().Be(1);
        }
    }

    [Fact]
    public async Task Tombstone_WhenRecordsStoreThrows_RollsBack_LogAndAudit_AndIsRetryableWithSameCommandId()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_ATOMICITY_TOMBSTONE_STORE_THROW";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var (dims, dimSetId) = CreateBuildingKey();

        // seed an initial active record
        await using (var seedScope = host.Services.CreateAsyncScope())
        {
            var svc = seedScope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var seedCmd = Guid.CreateVersion7();
            var res = await svc.UpsertAsync(
                registerId,
                dims,
                periodUtc: null,
                values: new Dictionary<string, object?> { ["amount"] = 123 },
                commandId: seedCmd,
                manageTransaction: true,
                ct: CancellationToken.None);

            res.Should().Be(ReferenceRegisterWriteResult.Executed);
        }

        var table = await ResolveRecordsTableAsync(host, registerId);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var versions = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @DimSetId;",
                new { DimSetId = dimSetId });

            versions.Should().Be(1);
        }

        var baselineAuditEvents = await CountAuditEventsAsync(Fixture.ConnectionString);

        var cmd = Guid.CreateVersion7();
        using var failingHost = CreateHostWithThrowingStore(Fixture.ConnectionString);

        await using (var scope = failingHost.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var act = () => svc.TombstoneAsync(
                registerId,
                dims,
                asOfUtc: DateTime.UtcNow,
                commandId: cmd,
                manageTransaction: true,
                ct: CancellationToken.None);

            await act.Should()
                .ThrowAsync<NotSupportedException>()
                .WithMessage("*BOOM: records store append*");
        }

        // rollback => no audit, no log row, no extra version
        (await CountAuditEventsAsync(Fixture.ConnectionString)).Should().Be(baselineAuditEvents);

        (await CountIndependentWriteLogRowsAsync(
                Fixture.ConnectionString,
                registerId,
                cmd,
                ReferenceRegisterIndependentWriteOperation.Tombstone))
            .Should()
            .Be(0);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var versionsAfterFailure = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @DimSetId;",
                new { DimSetId = dimSetId });

            versionsAfterFailure.Should().Be(1);
        }

        // retry with healthy host, same commandId must succeed
        using var healthyHost = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using (var scope = healthyHost.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var res = await svc.TombstoneAsync(
                registerId,
                dims,
                asOfUtc: DateTime.UtcNow,
                commandId: cmd,
                manageTransaction: true,
                ct: CancellationToken.None);

            res.Should().Be(ReferenceRegisterWriteResult.Executed);
        }

        (await CountAuditEventsAsync(Fixture.ConnectionString)).Should().Be(baselineAuditEvents + 1);

        (await CountIndependentWriteLogRowsAsync(
                Fixture.ConnectionString,
                registerId,
                cmd,
                ReferenceRegisterIndependentWriteOperation.Tombstone))
            .Should()
            .Be(1);

        (await IsIndependentWriteLogCompletedAsync(
                Fixture.ConnectionString,
                registerId,
                cmd,
                ReferenceRegisterIndependentWriteOperation.Tombstone))
            .Should()
            .BeTrue();

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var versionsAfterSuccess = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @DimSetId;",
                new { DimSetId = dimSetId });

            versionsAfterSuccess.Should().Be(2);
        }
    }

    private static IHost CreateHostWithThrowingStore(string connectionString)
    {
        return IntegrationHostFactory.Create(
            connectionString,
            services =>
            {
                services.RemoveAll<IReferenceRegisterRecordsStore>();
                services.AddScoped<IReferenceRegisterRecordsStore, ThrowingRecordsStore>();
            });
    }

    private sealed class ThrowingRecordsStore : IReferenceRegisterRecordsStore
    {
        public Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default) => Task.CompletedTask;

        public Task AppendAsync(Guid registerId, IReadOnlyList<ReferenceRegisterRecordWrite> records, CancellationToken ct = default)
            => throw new NotSupportedException("BOOM: records store append");
    }

    private static async Task<int> CountAuditEventsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
    }

    private static async Task<int> CountIndependentWriteLogRowsAsync(
        string connectionString,
        Guid registerId,
        Guid commandId,
        ReferenceRegisterIndependentWriteOperation operation)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        return await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM reference_register_independent_write_state
            WHERE register_id = @RegisterId
              AND command_id = @CommandId
              AND operation = @Operation;
            """,
            new
            {
                RegisterId = registerId,
                CommandId = commandId,
                Operation = (short)operation
            });
    }

    private static async Task<bool> IsIndependentWriteLogCompletedAsync(
        string connectionString,
        Guid registerId,
        Guid commandId,
        ReferenceRegisterIndependentWriteOperation operation)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var completed = await conn.ExecuteScalarAsync<DateTime?>(
            """
            SELECT completed_at_utc
            FROM reference_register_independent_write_state
            WHERE register_id = @RegisterId
              AND command_id = @CommandId
              AND operation = @Operation;
            """,
            new
            {
                RegisterId = registerId,
                CommandId = commandId,
                Operation = (short)operation
            });

        return completed is not null;
    }

    private static (IReadOnlyList<DimensionValue> Dimensions, Guid DimensionSetId) CreateBuildingKey()
    {
        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var buildingValueId = DeterministicGuid.Create("Building|A");

        var dims = new[] { new DimensionValue(buildingDimId, buildingValueId) };
        var setId = DeterministicDimensionSetId.FromBag(new DimensionBag(dims));

        return (dims, setId);
    }

    private static async Task<Guid> ArrangeIndependentRegisterAsync(
        IHost host,
        string code,
        ReferenceRegisterPeriodicity periodicity = ReferenceRegisterPeriodicity.NonPeriodic,
        bool withDimensionRules = true)
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

        if (withDimensionRules)
        {
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
        }

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
}
