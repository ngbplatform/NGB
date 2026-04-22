using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterRecordsSchema_FieldNullabilityDriftRepair_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ReplaceFields_WhenNoRecords_DriftRepairsColumnNullability()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_NULLABILITY_DRIFT";
        Guid registerId;

        // Create register and initial fields (nullable).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "Nullability drift repair",
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
                        Metadata.Base.ColumnType.Decimal,
                        true)
                ],
                ct: CancellationToken.None);
        }

        var tableCode = ReferenceRegisterNaming.NormalizeTableCode(code);
        var tableName = $"refreg_{tableCode}__records";
        var column = ReferenceRegisterNaming.NormalizeColumnCode("amount");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var isNullable = await uow.Connection.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    """
                    SELECT is_nullable
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = @Table
                      AND column_name = @Column;
                    """,
                    new { Table = tableName, Column = column },
                    cancellationToken: CancellationToken.None));

            isNullable.Should().Be("YES");
        }

        // Replace fields with NOT NULL (still allowed because HasRecords=false).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            await svc.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition(
                        "amount",
                        "Amount",
                        10,
                        Metadata.Base.ColumnType.Decimal,
                        false)
                ],
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var isNullable = await uow.Connection.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    """
                    SELECT is_nullable
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = @Table
                      AND column_name = @Column;
                    """,
                    new { Table = tableName, Column = column },
                    cancellationToken: CancellationToken.None));

            isNullable.Should().Be("NO");
        }
    }
}
