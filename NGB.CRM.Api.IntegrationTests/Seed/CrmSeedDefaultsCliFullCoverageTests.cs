using FluentAssertions;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using NGB.CRM.Migrator.Seed;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Seed;

[Collection(CrmSeedPostgresCollection.Name)]
public sealed class CrmSeedDefaultsCliFullCoverageTests(CrmPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void CommandParsingAndServiceCreation_CoverBoundaries()
    {
        CrmSeedDefaultsCli.IsSeedDefaultsCommand([]).Should().BeFalse();
        CrmSeedDefaultsCli.IsSeedDefaultsCommand(["other"]).Should().BeFalse();
        CrmSeedDefaultsCli.IsSeedDefaultsCommand(["SEED-DEFAULTS"]).Should().BeTrue();
        CrmSeedDefaultsCli.TrimCommand([]).Should().BeEmpty();
        CrmSeedDefaultsCli.TrimCommand(["seed-defaults"]).Should().BeEmpty();
        CrmSeedDefaultsCli.TrimCommand(["seed-defaults", "--one", "two"]).Should().Equal("--one", "two");
        CrmSeedDefaultsCli.CreateServices(fixture.ConnectionString).Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunAsync_CoversInvalidAndSuccessfulIdempotentSetup()
    {
        (await CrmSeedDefaultsCli.RunAsync(["--connection=not-a-connection-string"])).Should().Be(1);
        (await CrmSeedDefaultsCli.RunAsync(["--connection", fixture.ConnectionString])).Should().Be(0);
        (await CrmSeedDefaultsCli.RunAsync(["--connection", fixture.ConnectionString])).Should().Be(0);
    }
}
