using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Admin endpoint surface contracts (list/details/ensure-for-all) for Operational Registers.
///
/// Why P0:
/// - This endpoint is intended to be used directly by Web API / UI.
/// - We want stable contract: list counts, details shape, and bulk remediation.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterAdminEndpoint_ListDetails_And_EnsureForAll_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetListAsync_WhenNoRegisters_ReturnsEmpty()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

        var list = await endpoint.GetListAsync(CancellationToken.None);
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task GetListAsync_WhenRegistersExist_ReturnsItemsWithCounts()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (aId, bId) = await CreateTwoRegistersWithDifferentMetaAsync(host, CancellationToken.None);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

        var list = await endpoint.GetListAsync(CancellationToken.None);
        list.Should().HaveCount(2);

        var a = list.Single(x => x.Register.RegisterId == aId);
        var b = list.Single(x => x.Register.RegisterId == bId);

        a.Register.Code.Should().Be("OR_ADMIN_A");
        a.ResourcesCount.Should().Be(2);
        a.DimensionRulesCount.Should().Be(1);

        b.Register.Code.Should().Be("OR_ADMIN_B");
        b.ResourcesCount.Should().Be(1);
        b.DimensionRulesCount.Should().Be(0);
    }

    [Fact]
    public async Task GetDetailsByIdAsync_WhenExisting_ReturnsResourcesAndDimensionRules()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (aId, _) = await CreateTwoRegistersWithDifferentMetaAsync(host, CancellationToken.None);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

        var details = await endpoint.GetDetailsByIdAsync(aId, CancellationToken.None);
        details.Should().NotBeNull();

        details!.Register.RegisterId.Should().Be(aId);
        details.Register.Code.Should().Be("OR_ADMIN_A");
        details.Register.Name.Should().Be("Admin A");

        details.Resources.Should().HaveCount(2);
        details.Resources.Should().ContainSingle(r => r.Code == "Amount" && r.ColumnCode == OperationalRegisterNaming.NormalizeColumnCode("Amount"));
        details.Resources.Should().ContainSingle(r => r.Code == "Tax" && r.ColumnCode == OperationalRegisterNaming.NormalizeColumnCode("Tax"));

        details.DimensionRules.Should().HaveCount(1);
        details.DimensionRules[0].DimensionCode.Should().Be("DEPT");
        details.DimensionRules[0].Ordinal.Should().Be(10);
        details.DimensionRules[0].IsRequired.Should().BeTrue();
    }

    [Fact]
    public async Task GetDetailsByCodeAsync_WhenUnknown_ReturnsNull()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

        var details = await endpoint.GetDetailsByCodeAsync("NO_SUCH_REGISTER", CancellationToken.None);
        details.Should().BeNull();
    }

    [Fact]
    public async Task EnsurePhysicalSchemaById_WhenUnknown_ReturnsNull()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

        var health = await endpoint.EnsurePhysicalSchemaByIdAsync(Guid.CreateVersion7(), CancellationToken.None);
        health.Should().BeNull();
    }

    [Fact]
    public async Task EnsurePhysicalSchemaForAll_WhenSomeTablesDropped_RecreatesAndAllOk()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (aId, bId) = await CreateTwoRegistersWithDifferentMetaAsync(host, CancellationToken.None);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

            // First ensure: create all physical tables.
            var first = await endpoint.EnsurePhysicalSchemaForAllAsync(CancellationToken.None);
            first.TotalCount.Should().Be(2);
            first.OkCount.Should().Be(2);
        }

        // Simulate drift: drop movements table for register A.
        await DropPerRegisterTableAsync(
            Fixture.ConnectionString,
            aId,
            tableSuffix: "__movements",
            ct: CancellationToken.None);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

            var report = await endpoint.EnsurePhysicalSchemaForAllAsync(CancellationToken.None);
            report.TotalCount.Should().Be(2);
            report.OkCount.Should().Be(2);

            report.Items.Should().HaveCount(2);
            report.Items.Single(x => x.Register.RegisterId == aId).IsOk.Should().BeTrue();
            report.Items.Single(x => x.Register.RegisterId == bId).IsOk.Should().BeTrue();

            // Double-check by id.
            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(aId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.IsOk.Should().BeTrue();
            health.Movements.Exists.Should().BeTrue();
            health.Movements.HasAppendOnlyGuard.Should().BeTrue();
        }
    }

    private static async Task<(Guid AId, Guid BId)> CreateTwoRegistersWithDifferentMetaAsync(IHost host, CancellationToken ct)
    {
        Guid aId;
        Guid bId;

        // Register A: 2 resources, 1 dimension rule.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            aId = await mgmt.UpsertAsync(code: "OR_ADMIN_A", name: "Admin A", ct: ct);

            await mgmt.ReplaceResourcesAsync(
                aId,
                new[]
                {
                    new OperationalRegisterResourceDefinition("Amount", "Amount", Ordinal: 10),
                    new OperationalRegisterResourceDefinition("Tax", "Tax", Ordinal: 20)
                },
                ct);

            var deptId = DeterministicGuid.Create("Dimension|dept");

            await mgmt.ReplaceDimensionRulesAsync(
                aId,
                new[]
                {
                    new OperationalRegisterDimensionRule(deptId, "DEPT", Ordinal: 10, IsRequired: true)
                },
                ct);
        }

        // Register B: 1 resource, no dimension rules.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            bId = await mgmt.UpsertAsync(code: "OR_ADMIN_B", name: "Admin B", ct: ct);

            await mgmt.ReplaceResourcesAsync(
                bId,
                new[]
                {
                    new OperationalRegisterResourceDefinition("Amount", "Amount", Ordinal: 10)
                },
                ct);
        }

        return (aId, bId);
    }

    private static async Task DropPerRegisterTableAsync(
        string connectionString,
        Guid registerId,
        string tableSuffix,
        CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        const string sql = "SELECT table_code FROM operational_registers WHERE register_id = @Id";
        var tableCode = await conn.ExecuteScalarAsync<string?>(sql, new { Id = registerId });
        tableCode.Should().NotBeNull("register must exist");

        // NOTE: table_code is already collision-safe and within identifier limits.
        var table = $"opreg_{tableCode}{tableSuffix}";
        await conn.ExecuteAsync(new CommandDefinition($"DROP TABLE IF EXISTS {table};", cancellationToken: ct));
    }
}
