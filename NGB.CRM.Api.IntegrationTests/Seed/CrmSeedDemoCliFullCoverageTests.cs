using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using NGB.CRM.Migrator.Seed;
using NGB.CRM.Runtime;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Seed;

[Collection(CrmSeedPostgresCollection.Name)]
public sealed class CrmSeedDemoCliFullCoverageTests(CrmPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void CommandParsing_CoversEmptyMismatchCaseInsensitivityAndTrimBoundaries()
    {
        CrmSeedDemoCli.IsSeedDemoCommand([]).Should().BeFalse();
        CrmSeedDemoCli.IsSeedDemoCommand(["other"]).Should().BeFalse();
        CrmSeedDemoCli.IsSeedDemoCommand(["SEED-DEMO"]).Should().BeTrue();

        CrmSeedDemoCli.TrimCommand([]).Should().BeEmpty();
        CrmSeedDemoCli.TrimCommand(["seed-demo"]).Should().BeEmpty();
        CrmSeedDemoCli.TrimCommand(["seed-demo", "--one", "two"]).Should().Equal("--one", "two");
    }

    [Fact]
    public async Task RunAsync_CoversFailureAndIdempotentSuccessWithRepresentativeProfile()
    {
        (await CrmSeedDemoCli.RunAsync(["--connection=not-a-connection-string"])).Should().Be(1);

        ServiceCollection CreateServices(string connectionString)
        {
            var services = CrmSeedDefaultsCli.CreateServices(connectionString);
            services.RemoveAll<CrmDemoSeedOptions>();
            services.AddSingleton(new CrmDemoSeedOptions
            {
                GeneratedAccountCount = 1,
                GeneratedOpportunityCycleCount = 1
            });
            return services;
        }

        var args = new[] { "--connection", fixture.ConnectionString };
        (await CrmSeedDemoCli.RunAsync(args, CreateServices)).Should().Be(0);
        (await CrmSeedDemoCli.RunAsync(args, CreateServices)).Should().Be(0);
    }
}
