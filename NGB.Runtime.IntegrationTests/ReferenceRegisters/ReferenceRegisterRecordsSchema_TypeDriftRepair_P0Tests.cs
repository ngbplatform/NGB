using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterRecordsSchema_TypeDriftRepair_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureSchema_WhenNoRecords_DriftRepairsInt32ColumnType()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_TYPE_DRIFT_NO_RECORDS";
        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await svc.UpsertAsync(
                code,
                name: "RR type drift (no records)",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition("qty", "Qty", 10, ColumnType.Int32, true),
                ],
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        var tableCode = ReferenceRegisterNaming.NormalizeTableCode(code);
        var tableName = $"refreg_{tableCode}__records";
        var column = ReferenceRegisterNaming.NormalizeColumnCode("qty");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    $"ALTER TABLE {tableName} ALTER COLUMN {column} TYPE BIGINT USING {column}::BIGINT;",
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var udt = await uow.Connection.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    """
                    SELECT udt_name
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = @Table
                      AND column_name = @Column;
                    """,
                    new { Table = tableName, Column = column },
                    cancellationToken: CancellationToken.None));

            udt.Should().Be("int4");
        }
    }

    [Fact]
    public async Task EnsureSchema_WhenRecordsExist_ThrowsOnInt32ColumnTypeDrift()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_TYPE_DRIFT_HAS_RECORDS";
        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await svc.UpsertAsync(
                code,
                name: "RR type drift (has records)",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition("qty", "Qty", 10, ColumnType.Int32, true),
                ],
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                await store.AppendAsync(
                    registerId,
                    records:
                    [
                        new ReferenceRegisterRecordWrite(
                            DimensionSetId: Guid.Empty,
                            PeriodUtc: null,
                            RecorderDocumentId: null,
                            Values: new Dictionary<string, object?> { ["qty"] = 1 },
                            IsDeleted: false)
                    ],
                    ct);
            }, CancellationToken.None);
        }

        var tableCode = ReferenceRegisterNaming.NormalizeTableCode(code);
        var tableName = $"refreg_{tableCode}__records";
        var column = ReferenceRegisterNaming.NormalizeColumnCode("qty");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    $"ALTER TABLE {tableName} ALTER COLUMN {column} TYPE BIGINT USING {column}::BIGINT;",
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            var act = async () => await store.EnsureSchemaAsync(registerId, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ReferenceRegisterSchemaDriftAfterRecordsExistException>();
            ex.Which.AssertNgbError(ReferenceRegisterSchemaDriftAfterRecordsExistException.Code, "registerId", "table", "reason");
            ex.Which.AssertReason("type_mismatch");
        }
    }
}
