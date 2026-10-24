using Microsoft.Extensions.DependencyInjection;
using NGB.AgencyBilling.Migrator.Seed;
using NGB.AgencyBilling.Runtime;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;

[Collection(AgencyBillingApiProgramEnvironmentCollection.Name)]
public sealed class McpVerticalTests
{
    [Fact]
    public async Task ActualHostExposesOnlyInstalledAuthorizedReadsThroughNativeMcpClient()
    {
        var database = new AgencyBillingPostgresFixture();
        try
        {
            await database.InitializeAsync();
            using (var seedHost = AgencyBillingHostFactory.Create(database.ConnectionString))
            {
                await using var scope = seedHost.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>().EnsureDefaultsAsync();
            }
            var seeded = await AgencyBillingSeedDemoCli.RunAsync(
            [
                "--connection", database.ConnectionString,
                "--seed", "20260417",
                "--from", "2026-01-01",
                "--to", "2026-01-31",
                "--clients", "2",
                "--team-members", "3",
                "--projects", "2",
                "--timesheets", "5",
                "--sales-invoices", "2",
                "--customer-payments", "2"
            ]);
            Assert.Equal(0, seeded);
            await VerticalMcpContract.VerifyAsync<Program>(database.ConnectionString, "ab.");
            await VerticalMcpContract.VerifyAsync<Program>(database.ConnectionString, "ab.", contentEnabled: true);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }
}
