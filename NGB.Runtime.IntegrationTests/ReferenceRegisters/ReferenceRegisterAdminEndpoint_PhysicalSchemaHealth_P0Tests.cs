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

/// <summary>
/// P0: Reference Registers admin endpoint should surface physical schema health for per-register tables.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterAdminEndpoint_PhysicalSchemaHealth_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Health_IsOk_AfterEnsure()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_ADMIN_HEALTH_OK";
        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await mgmt.UpsertAsync(
                code,
                name: "Admin health OK",
                periodicity: ReferenceRegisterPeriodicity.Month,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, Metadata.Base.ColumnType.Decimal, true),
                    new ReferenceRegisterFieldDefinition("note", "Note", 20, Metadata.Base.ColumnType.String, true),
                ],
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();
            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);

            health.Should().NotBeNull();
            health!.IsOk.Should().BeTrue();
            health.Records.Exists.Should().BeTrue();
            health.Records.IsOk.Should().BeTrue();
            health.Records.MissingColumns.Should().BeEmpty();
            health.Records.MissingIndexes.Should().BeEmpty();
            health.Records.HasAppendOnlyGuard.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Health_Reports_MissingColumns_WhenPhysicalColumnIsDropped()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_ADMIN_HEALTH_MISSING_COL";
        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await mgmt.UpsertAsync(
                code,
                name: "Admin health missing column",
                periodicity: ReferenceRegisterPeriodicity.Day,
                recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
                ct: CancellationToken.None);

            await mgmt.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, Metadata.Base.ColumnType.Decimal, true),
                ],
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var tableCode = ReferenceRegisterNaming.NormalizeTableCode(code);
            var tableName = $"refreg_{tableCode}__records";
            var col = ReferenceRegisterNaming.NormalizeColumnCode("amount");

            await uow.Connection.ExecuteAsync(new CommandDefinition(
                $"ALTER TABLE {tableName} DROP COLUMN {col};",
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();
            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);

            health.Should().NotBeNull();
            health!.IsOk.Should().BeFalse("a required field column was dropped");
            health.Records.MissingColumns.Should().Contain(ReferenceRegisterNaming.NormalizeColumnCode("amount"));
        }
    }
}
