using FluentAssertions;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Migrator.Seed;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Seed;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeSeedDefaultsCliFullCoverageTests(TradePostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void CommandParsing_CoversEmptyDifferentCaseAndTrailingArguments()
    {
        TradeSeedDefaultsCli.IsSeedDefaultsCommand([]).Should().BeFalse();
        TradeSeedDefaultsCli.IsSeedDefaultsCommand(["other"]).Should().BeFalse();
        TradeSeedDefaultsCli.IsSeedDefaultsCommand(["SEED-DEFAULTS"]).Should().BeTrue();
        TradeSeedDefaultsCli.TrimCommand([]).Should().BeEmpty();
        TradeSeedDefaultsCli.TrimCommand(["seed-defaults"]).Should().BeEmpty();
        TradeSeedDefaultsCli.TrimCommand(["seed-defaults", "--one", "two"]).Should().Equal("--one", "two");
    }

    [Fact]
    public void GetArgValue_CoversSeparateInlineMissingAndUnknownForms()
    {
        TradeSeedDefaultsCli.GetArgValue(["--CONNECTION", "value"], "--connection").Should().Be("value");
        TradeSeedDefaultsCli.GetArgValue(["--connection=inline"], "--connection").Should().Be("inline");
        TradeSeedDefaultsCli.GetArgValue(["--connection"], "--connection").Should().BeNull();
        TradeSeedDefaultsCli.GetArgValue(["--other", "value"], "--connection").Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_CoversMissingInvalidAndSuccessfulIdempotentSetup()
    {
        (await TradeSeedDefaultsCli.RunAsync(["--connection="])).Should().Be(2);
        (await TradeSeedDefaultsCli.RunAsync(["--connection=not-a-connection-string"])).Should().Be(1);
        (await TradeSeedDefaultsCli.RunAsync([], _ => fixture.ConnectionString)).Should().Be(0);
        (await TradeSeedDefaultsCli.RunAsync(["--connection", fixture.ConnectionString])).Should().Be(0);
        (await TradeSeedDefaultsCli.RunAsync(["--connection", fixture.ConnectionString])).Should().Be(0);
    }
}
