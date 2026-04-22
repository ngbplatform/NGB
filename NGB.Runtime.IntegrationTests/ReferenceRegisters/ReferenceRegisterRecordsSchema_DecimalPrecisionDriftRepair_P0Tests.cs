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
public sealed class ReferenceRegisterRecordsSchema_DecimalPrecisionDriftRepair_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureSchema_WhenNoRecords_DriftRepairsDecimalPrecisionAndScale()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_DECIMAL_PREC_DRIFT";
        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "Decimal precision drift repair",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition(
                        "amount",
                        "Amount",
                        10,
                        ColumnType.Decimal,
                        true)
                ],
                ct: CancellationToken.None);
        }

        var tableCode = ReferenceRegisterNaming.NormalizeTableCode(code);
        var tableName = $"refreg_{tableCode}__records";
        var column = ReferenceRegisterNaming.NormalizeColumnCode("amount");

        // Introduce drift: set DECIMAL column to plain NUMERIC without precision/scale.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    $"ALTER TABLE {tableName} ALTER COLUMN {column} TYPE NUMERIC USING {column}::NUMERIC;",
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));
        }

        // EnsureSchema should drift-repair NUMERIC -> NUMERIC(28,8) when HasRecords=false.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var meta = await uow.Connection.QuerySingleAsync<(int? Precision, int? Scale)>(
                new CommandDefinition(
                    """
                    SELECT numeric_precision AS "Precision", numeric_scale AS "Scale"
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = @Table
                      AND column_name = @Column;
                    """,
                    new { Table = tableName, Column = column },
                    cancellationToken: CancellationToken.None));

            meta.Precision.Should().Be(28);
            meta.Scale.Should().Be(8);
        }
    }

    [Fact]
    public async Task EnsureSchema_WhenRecordsExist_ThrowsOnDecimalPrecisionDrift()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_DECIMAL_PREC_DRIFT_HAS_RECORDS";
        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "Decimal precision drift repair (has records)",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition(
                        "amount",
                        "Amount",
                        10,
                        ColumnType.Decimal,
                        true)
                ],
                ct: CancellationToken.None);
        }

        var tableCode = ReferenceRegisterNaming.NormalizeTableCode(code);
        var tableName = $"refreg_{tableCode}__records";
        var column = ReferenceRegisterNaming.NormalizeColumnCode("amount");

        // Append a record to flip HasRecords=true.
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
                            Values: new Dictionary<string, object?>(),
                            IsDeleted: false)
                    ],
                    ct);
            }, CancellationToken.None);
        }

        // Introduce drift after records exist.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    $"ALTER TABLE {tableName} ALTER COLUMN {column} TYPE NUMERIC USING {column}::NUMERIC;",
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));
        }

        // EnsureSchema must not perform destructive drift-repair after HasRecords=true.
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
