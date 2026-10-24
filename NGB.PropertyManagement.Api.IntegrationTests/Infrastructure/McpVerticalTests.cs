using NGB.PropertyManagement.Migrator.Seed;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class McpProgramEnvironmentCollection
{
    public const string Name = "PM MCP Program environment";
}

[Collection(McpProgramEnvironmentCollection.Name)]
public sealed class McpVerticalTests
{
    [Fact]
    public async Task ActualHostExposesOnlyInstalledAuthorizedReadsThroughNativeMcpClient()
    {
        var database = new PmIntegrationFixture();
        try
        {
            await database.InitializeAsync();
            var seeded = await PropertyManagementSeedDemoCli.RunAsync(
            [
                "--connection", database.ConnectionString,
                "--dataset", "mcp",
                "--seed", "20260823",
                "--from", "2026-04-05",
                "--to", "2026-04-05",
                "--buildings", "1",
                "--units-min", "1",
                "--units-max", "1",
                "--tenants", "1",
                "--vendors", "1",
                "--occupancy-rate", "1.00"
            ], VerticalMcpContract.Clock);
            Assert.Equal(0, seeded);
            await VerticalMcpContract.VerifyAsync<Program>(database.ConnectionString, "pm.");
            await VerticalMcpContract.VerifyAsync<Program>(database.ConnectionString, "pm.", contentEnabled: true);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }
}
