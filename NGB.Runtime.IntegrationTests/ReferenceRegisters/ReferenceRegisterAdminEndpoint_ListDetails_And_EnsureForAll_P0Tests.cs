using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P0: Admin endpoint surface contracts (list/details/ensure-for-all) for Reference Registers.
///
/// Why P0:
/// - This endpoint is intended to be used directly by Web API / UI.
/// - We want a stable, provider-backed contract: list counts, details shape, and bulk remediation.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterAdminEndpoint_ListDetails_And_EnsureForAll_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetListAsync_WhenRegistersExist_ReturnsItemsWithCounts()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (aId, bId) = await CreateTwoRegistersWithDifferentMetaAsync(host, CancellationToken.None);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();

        var list = await endpoint.GetListAsync(CancellationToken.None);
        list.Should().HaveCount(2);

        var a = list.Single(x => x.Register.RegisterId == aId);
        var b = list.Single(x => x.Register.RegisterId == bId);

        a.Register.Code.Should().Be("RR_ADMIN_A");
        a.FieldsCount.Should().Be(2);
        a.DimensionRulesCount.Should().Be(1);

        b.Register.Code.Should().Be("RR_ADMIN_B");
        b.FieldsCount.Should().Be(1);
        b.DimensionRulesCount.Should().Be(0);
    }

    [Fact]
    public async Task GetDetailsByIdAsync_WhenExisting_ReturnsFieldsAndDimensionRules()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (aId, _) = await CreateTwoRegistersWithDifferentMetaAsync(host, CancellationToken.None);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();

        var details = await endpoint.GetDetailsByIdAsync(aId, CancellationToken.None);
        details.Should().NotBeNull();

        details!.Register.RegisterId.Should().Be(aId);
        details.Register.Code.Should().Be("RR_ADMIN_A");
        details.Register.Name.Should().Be("Admin A");
        details.Register.Periodicity.Should().Be(ReferenceRegisterPeriodicity.Month);
        details.Register.RecordMode.Should().Be(ReferenceRegisterRecordMode.Independent);

        details.Fields.Should().HaveCount(2);
        details.Fields.Should().ContainSingle(f => f.Code == "amount" && f.ColumnCode == ReferenceRegisterNaming.NormalizeColumnCode("amount"));
        details.Fields.Should().ContainSingle(f => f.Code == "note" && f.ColumnCode == ReferenceRegisterNaming.NormalizeColumnCode("note"));

        details.DimensionRules.Should().HaveCount(1);
        details.DimensionRules[0].DimensionCode.Should().Be("DEPT");
        details.DimensionRules[0].Ordinal.Should().Be(10);
    }

    [Fact]
    public async Task GetDetailsByCodeAsync_WhenUnknown_ReturnsNull()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();

        var details = await endpoint.GetDetailsByCodeAsync("NO_SUCH_REGISTER", CancellationToken.None);
        details.Should().BeNull();
    }

    [Fact]
    public async Task EnsurePhysicalSchemaById_WhenUnknown_ReturnsNull()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();

        var health = await endpoint.EnsurePhysicalSchemaByIdAsync(Guid.CreateVersion7(), CancellationToken.None);
        health.Should().BeNull();
    }

    [Fact]
    public async Task EnsurePhysicalSchemaForAll_WhenSomeRecordsTablesDropped_RecreatesAndAllOk()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (aId, bId) = await CreateTwoRegistersWithDifferentMetaAsync(host, CancellationToken.None);

        // Drop the physical records table for one register (simulate drift / manual damage).
        await DropRecordsTableAsync(Fixture.ConnectionString, aId, CancellationToken.None);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();

        var report = await endpoint.EnsurePhysicalSchemaForAllAsync(CancellationToken.None);
        report.TotalCount.Should().Be(2);
        report.OkCount.Should().Be(2);

        report.Items.Should().HaveCount(2);
        report.Items.Single(x => x.Register.RegisterId == aId).IsOk.Should().BeTrue();
        report.Items.Single(x => x.Register.RegisterId == bId).IsOk.Should().BeTrue();
    }

    private static async Task<(Guid AId, Guid BId)> CreateTwoRegistersWithDifferentMetaAsync(IHost host, CancellationToken ct)
    {
        Guid aId;
        Guid bId;

        // Register A: 2 fields, 1 dimension rule.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            aId = await mgmt.UpsertAsync(
                code: "RR_ADMIN_A",
                name: "Admin A",
                periodicity: ReferenceRegisterPeriodicity.Month,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: ct);

            await mgmt.ReplaceFieldsAsync(
                aId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, Metadata.Base.ColumnType.Decimal, true),
                    new ReferenceRegisterFieldDefinition("note", "Note", 20, Metadata.Base.ColumnType.String, true),
                ],
                ct: ct);

            await mgmt.ReplaceDimensionRulesAsync(
                aId,
                rules:
                [
                    new ReferenceRegisterDimensionRule(DeterministicGuid.Create("Dimension|dept"), "DEPT", 10, IsRequired: true),
                ],
                ct: ct);
        }

        // Register B: 1 field, no dimension rules.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            bId = await mgmt.UpsertAsync(
                code: "RR_ADMIN_B",
                name: "Admin B",
                periodicity: ReferenceRegisterPeriodicity.Day,
                recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
                ct: ct);

            await mgmt.ReplaceFieldsAsync(
                bId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, Metadata.Base.ColumnType.Decimal, true),
                ],
                ct: ct);
        }

        return (aId, bId);
    }

    private static async Task DropRecordsTableAsync(string connectionString, Guid registerId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // Read the per-register table_code from metadata; this avoids relying on NormalizeTableCode edge cases in tests.
        const string sql = "SELECT table_code FROM reference_registers WHERE register_id = @Id";
        var tableCode = await conn.ExecuteScalarAsync<string?>(sql, new { Id = registerId });
        tableCode.Should().NotBeNull("register must exist");

        var table = $"refreg_{tableCode}__records";
        await conn.ExecuteAsync(new CommandDefinition($"DROP TABLE IF EXISTS {table};", cancellationToken: ct));
    }
}
