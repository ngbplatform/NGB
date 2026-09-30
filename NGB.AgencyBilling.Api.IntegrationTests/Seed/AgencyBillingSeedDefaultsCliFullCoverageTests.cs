using FluentAssertions;
using NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;
using NGB.AgencyBilling.Migrator.Seed;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Seed;

[Collection(AgencyBillingPostgresCollection.Name)]
public sealed class AgencyBillingSeedDefaultsCliFullCoverageTests(AgencyBillingPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void CommandParsing_CoversEmptyDifferentCaseAndTrailingArguments()
    {
        AgencyBillingSeedDefaultsCli.IsSeedDefaultsCommand([]).Should().BeFalse();
        AgencyBillingSeedDefaultsCli.IsSeedDefaultsCommand(["other"]).Should().BeFalse();
        AgencyBillingSeedDefaultsCli.IsSeedDefaultsCommand(["SEED-DEFAULTS"]).Should().BeTrue();
        AgencyBillingSeedDefaultsCli.TrimCommand([]).Should().BeEmpty();
        AgencyBillingSeedDefaultsCli.TrimCommand(["seed-defaults"]).Should().BeEmpty();
        AgencyBillingSeedDefaultsCli.TrimCommand(["seed-defaults", "--one", "two"]).Should().Equal("--one", "two");
    }

    [Fact]
    public void GetArgValue_CoversSeparateInlineMissingAndUnknownForms()
    {
        AgencyBillingSeedDefaultsCli.GetArgValue(["--CONNECTION", "value"], "--connection").Should().Be("value");
        AgencyBillingSeedDefaultsCli.GetArgValue(["--connection=inline"], "--connection").Should().Be("inline");
        AgencyBillingSeedDefaultsCli.GetArgValue(["--connection"], "--connection").Should().BeNull();
        AgencyBillingSeedDefaultsCli.GetArgValue(["--other", "value"], "--connection").Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_CoversMissingInvalidAndSuccessfulIdempotentSetup()
    {
        (await AgencyBillingSeedDefaultsCli.RunAsync(["--connection="])).Should().Be(2);
        (await AgencyBillingSeedDefaultsCli.RunAsync(["--connection=not-a-connection-string"])).Should().Be(1);
        (await AgencyBillingSeedDefaultsCli.RunAsync([], _ => fixture.ConnectionString)).Should().Be(0);
        (await AgencyBillingSeedDefaultsCli.RunAsync(["--connection", fixture.ConnectionString])).Should().Be(0);
        (await AgencyBillingSeedDefaultsCli.RunAsync(["--connection", fixture.ConnectionString])).Should().Be(0);
    }
}
