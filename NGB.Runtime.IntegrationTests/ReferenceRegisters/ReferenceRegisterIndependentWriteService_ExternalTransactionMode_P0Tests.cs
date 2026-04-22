using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.AuditLog;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Persistence.AuditLog;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterIndependentWriteService_ExternalTransactionMode_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpsertAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_EXT_TX_NTX";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var (dims, _) = CreateBuildingKey();

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

        var act = () => svc.UpsertAsync(
            registerId,
            dims,
            periodUtc: null,
            values: new Dictionary<string, object?> { ["amount"] = 1 },
            commandId: Guid.CreateVersion7(),
            manageTransaction: false,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task UpsertAsync_ManageTransactionFalse_WhenOuterTransactionRollsBack_DoesNotPersistRecords_LogOrAudit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_EXT_TX_RB";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var table = await ResolveRecordsTableAsync(host, registerId);

        var (dims, dimSetId) = CreateBuildingKey();
        var cmd = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                var res = await svc.UpsertAsync(
                    registerId,
                    dims,
                    periodUtc: null,
                    values: new Dictionary<string, object?> { ["amount"] = 10 },
                    commandId: cmd,
                    manageTransaction: false,
                    ct: CancellationToken.None);

                res.Should().Be(ReferenceRegisterWriteResult.Executed);

                var logCountInTx = await uow.Connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        """
                        SELECT COUNT(*)
                        FROM reference_register_independent_write_state
                        WHERE register_id = @registerId AND command_id = @cmd AND operation = 1;
                        """,
                        new { registerId, cmd },
                        uow.Transaction,
                        cancellationToken: CancellationToken.None));

                logCountInTx.Should().Be(1);

                var recordCountInTx = await uow.Connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @dimSetId;",
                        new { dimSetId },
                        uow.Transaction,
                        cancellationToken: CancellationToken.None));

                recordCountInTx.Should().Be(1);

                var auditCountInTx = await uow.Connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        """
                        SELECT COUNT(*)
                        FROM platform_audit_events
                        WHERE entity_kind = @kind AND entity_id = @registerId AND action_code = @actionCode;
                        """,
                        new
                        {
                            kind = (short)AuditEntityKind.ReferenceRegister,
                            registerId,
                            actionCode = AuditActionCodes.ReferenceRegisterRecordsUpsert
                        },
                        uow.Transaction,
                        cancellationToken: CancellationToken.None));

                auditCountInTx.Should().Be(1);
            }
            finally
            {
                await uow.RollbackAsync(CancellationToken.None);
            }
        }

        // Assert: nothing committed.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var logCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM reference_register_independent_write_state WHERE register_id = @registerId;",
                new { registerId });

            logCount.Should().Be(0);

            var recordCount = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @dimSetId;",
                new { dimSetId });

            recordCount.Should().Be(0);

            var auditCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM platform_audit_events
                WHERE entity_kind = @kind AND entity_id = @registerId AND action_code = @actionCode;
                """,
                new
                {
                    kind = (short)AuditEntityKind.ReferenceRegister,
                    registerId,
                    actionCode = AuditActionCodes.ReferenceRegisterRecordsUpsert
                });

            auditCount.Should().Be(0);
        }

        // Assert: audit reader returns nothing.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ReferenceRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.ReferenceRegisterRecordsUpsert,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task UpsertAsync_ManageTransactionFalse_WhenOuterTransactionCommits_PersistsRecords_LogAndAudit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IND_EXT_TX_COMMIT";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var table = await ResolveRecordsTableAsync(host, registerId);

        var (dims, dimSetId) = CreateBuildingKey();
        var cmd = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                var res = await svc.UpsertAsync(
                    registerId,
                    dims,
                    periodUtc: null,
                    values: new Dictionary<string, object?> { ["amount"] = 77 },
                    commandId: cmd,
                    manageTransaction: false,
                    ct: CancellationToken.None);

                res.Should().Be(ReferenceRegisterWriteResult.Executed);

                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                await uow.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        // Assert: committed state exists.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var logCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM reference_register_independent_write_state WHERE register_id = @registerId AND command_id = @cmd AND operation = 1;",
                new { registerId, cmd });

            logCount.Should().Be(1);

            var recordCount = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {table} WHERE dimension_set_id = @dimSetId;",
                new { dimSetId });

            recordCount.Should().Be(1);

            var auditCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM platform_audit_events
                WHERE entity_kind = @kind AND entity_id = @registerId AND action_code = @actionCode;
                """,
                new
                {
                    kind = (short)AuditEntityKind.ReferenceRegister,
                    registerId,
                    actionCode = AuditActionCodes.ReferenceRegisterRecordsUpsert
                });

            auditCount.Should().Be(1);
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

    private static async Task<Guid> ArrangeIndependentRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var registerId = await mgmt.UpsertAsync(
            code,
            name: $"{code} name",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
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
}
