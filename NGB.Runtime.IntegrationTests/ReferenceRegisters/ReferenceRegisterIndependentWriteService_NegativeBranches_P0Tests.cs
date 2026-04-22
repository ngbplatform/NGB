using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterIndependentWriteService_NegativeBranches_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Upsert_WhenLogRowIsInProgressFresh_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_INPROG_UPSERT";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var (dims, _) = CreateBuildingKey();
        var cmd = Guid.CreateVersion7();

        await InsertInProgressLogRowAsync(registerId, cmd, ReferenceRegisterIndependentWriteOperation.Upsert);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

        var act = () => svc.UpsertAsync(
            registerId,
            dims,
            periodUtc: null,
            values: new Dictionary<string, object?> { ["amount"] = 1 },
            commandId: cmd,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ReferenceRegisterIndependentWriteAlreadyInProgressException>();
        ex.Which.AssertNgbError(ReferenceRegisterIndependentWriteAlreadyInProgressException.Code, "registerId", "commandId", "operation");
    }

    [Fact]
    public async Task Tombstone_WhenLogRowIsInProgressFresh_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_INPROG_TOMBSTONE";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var (dims, _) = CreateBuildingKey();
        var cmd = Guid.CreateVersion7();

        await InsertInProgressLogRowAsync(registerId, cmd, ReferenceRegisterIndependentWriteOperation.Tombstone);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

        var act = () => svc.TombstoneAsync(
            registerId,
            dims,
            asOfUtc: DateTime.UtcNow,
            commandId: cmd,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ReferenceRegisterIndependentWriteAlreadyInProgressException>();
        ex.Which.AssertNgbError(ReferenceRegisterIndependentWriteAlreadyInProgressException.Code, "registerId", "commandId", "operation");
    }

    [Fact]
    public async Task Tombstone_WhenNoActiveRecord_IsNoOp_DoesNotAppend_AndDoesNotAudit_ButCompletesLog()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_TOMBSTONE_NOOP";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var (dims, dimSetId) = CreateBuildingKey();
        var cmd = Guid.CreateVersion7();

        int baselineEvents;
        int baselineChanges;

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            baselineEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
            baselineChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        }

        // Act: tombstone without any existing record versions.
        await using (var scope = host.Services.CreateAsyncScope())
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

        // Assert: no records appended.
        var table = await ResolveRecordsTableAsync(host, registerId);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var versions = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @DimSetId;",
                new { DimSetId = dimSetId });

            versions.Should().Be(0);

            // Assert: no audit event for no-op tombstone.
            var eventsNow = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
            var changesNow = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");

            eventsNow.Should().Be(baselineEvents);
            changesNow.Should().Be(baselineChanges);

            // Assert: write state row exists and is completed.
            var completedAt = await conn.ExecuteScalarAsync<DateTime?>(
                """
                SELECT completed_at_utc
                FROM reference_register_independent_write_state
                WHERE register_id = @RegisterId AND command_id = @CommandId AND operation = 2;
                """,
                new { RegisterId = registerId, CommandId = cmd });

            completedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Upsert_NonPeriodic_WithPeriodUtc_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_UPSERT_NONPER_WITH_PERIOD";
        var registerId = await ArrangeIndependentRegisterAsync(host, code, ReferenceRegisterPeriodicity.NonPeriodic);

        var (dims, _) = CreateBuildingKey();

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

        var act = () => svc.UpsertAsync(
            registerId,
            dims,
            periodUtc: DateTime.UtcNow,
            values: new Dictionary<string, object?> { ["amount"] = 1 },
            commandId: Guid.CreateVersion7(),
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
        ex.Which.AssertReason("period_not_allowed_for_non_periodic");
    }

    [Fact]
    public async Task Upsert_Periodic_WithoutPeriodUtc_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_UPSERT_PERIODIC_NO_PERIOD";
        var registerId = await ArrangeIndependentRegisterAsync(host, code, ReferenceRegisterPeriodicity.Day);

        var (dims, _) = CreateBuildingKey();

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

        var act = () => svc.UpsertAsync(
            registerId,
            dims,
            periodUtc: null,
            values: new Dictionary<string, object?> { ["amount"] = 1 },
            commandId: Guid.CreateVersion7(),
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
        ex.Which.AssertReason("period_required_for_periodic");
    }

    [Fact]
    public async Task Upsert_DimensionSetHasExtraDimension_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_DIM_EXTRA";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var buildingValueId = DeterministicGuid.Create("Building|A");

        var floorDimId = DeterministicGuid.Create("Dimension|floor");
        await EnsurePlatformDimensionExistsAsync(floorDimId, "floor", "Floor");

        var dims = new[]
        {
            new DimensionValue(buildingDimId, buildingValueId),
            new DimensionValue(floorDimId, Guid.CreateVersion7())
        };

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

        var act = () => svc.UpsertAsync(
            registerId,
            dims,
            periodUtc: null,
            values: new Dictionary<string, object?> { ["amount"] = 1 },
            commandId: Guid.CreateVersion7(),
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
        ex.Which.AssertReason("extra_dimensions");
    }

    [Fact]
    public async Task Upsert_DimensionSetMissingRequiredDimension_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_DIM_MISSING_REQUIRED";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var dims = Array.Empty<DimensionValue>();

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

        var act = () => svc.UpsertAsync(
            registerId,
            dims,
            periodUtc: null,
            values: new Dictionary<string, object?> { ["amount"] = 1 },
            commandId: Guid.CreateVersion7(),
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
        ex.Which.AssertReason("missing_required_dimensions");
    }

    [Fact]
    public async Task Upsert_WhenRegisterHasNoDimensionRules_ButDimensionSetIsNonEmpty_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_DIM_NORULES";
        var registerId = await ArrangeIndependentRegisterAsync(host, code, withDimensionRules: false);

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        await EnsurePlatformDimensionExistsAsync(buildingDimId, "building", "Building");

        var dims = new[]
        {
            new DimensionValue(buildingDimId, DeterministicGuid.Create("Building|A"))
        };

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

        var act = () => svc.UpsertAsync(
            registerId,
            dims,
            periodUtc: null,
            values: new Dictionary<string, object?> { ["amount"] = 1 },
            commandId: Guid.CreateVersion7(),
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
        ex.Which.AssertReason("dimension_not_allowed");
    }

    private async Task InsertInProgressLogRowAsync(Guid registerId, Guid commandId, ReferenceRegisterIndependentWriteOperation operation)
    {
        var startedAtUtc = DateTime.UtcNow.AddMinutes(-1);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        await conn.ExecuteAsync(
            """
            INSERT INTO reference_register_independent_write_state(register_id, command_id, operation, started_at_utc, completed_at_utc)
            VALUES (@RegisterId, @CommandId, @Operation, @StartedAtUtc, NULL)
            ON CONFLICT (register_id, command_id, operation) DO NOTHING;
            """,
            new
            {
                RegisterId = registerId,
                CommandId = commandId,
                Operation = (short)operation,
                StartedAtUtc = startedAtUtc
            });
    }

    private async Task EnsurePlatformDimensionExistsAsync(Guid dimensionId, string code, string name)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimensions(dimension_id, code, name)
            VALUES (@Id, @Code, @Name)
            ON CONFLICT (dimension_id) DO NOTHING;
            """,
            new { Id = dimensionId, Code = code.Trim(), Name = name.Trim() });
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
