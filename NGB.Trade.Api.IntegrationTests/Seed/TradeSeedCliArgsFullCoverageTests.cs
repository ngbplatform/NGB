using FluentAssertions;
using NGB.Tools.Exceptions;
using NGB.Trade.Migrator.Seed;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Seed;

[CollectionDefinition(TradeEnvironmentCollection.Name, DisableParallelization = true)]
public sealed class TradeEnvironmentCollection
{
    public const string Name = "Trade environment variable tests";
}

[Collection(TradeEnvironmentCollection.Name)]
public sealed class TradeSeedCliArgsFullCoverageTests
{
    [Fact]
    public void Parser_CoversEveryValueShapeAndFailure()
    {
        TradeSeedCliArgs.GetArgValue(["--COUNT", "7"], "--count").Should().Be("7");
        TradeSeedCliArgs.GetArgValue(["--count=8"], "--count").Should().Be("8");
        TradeSeedCliArgs.GetArgValue(["--count"], "--count").Should().BeNull();
        TradeSeedCliArgs.GetArgValue(["--other", "7"], "--count").Should().BeNull();

        TradeSeedCliArgs.GetInt([], "--count", 3).Should().Be(3);
        TradeSeedCliArgs.GetInt(["--count", "-7"], "--count", 3).Should().Be(-7);
        ((Action)(() => TradeSeedCliArgs.GetInt(["--count=invalid"], "--count", 3)))
            .Should().Throw<NgbArgumentInvalidException>();

        TradeSeedCliArgs.GetBool([], "--enabled", true).Should().BeTrue();
        TradeSeedCliArgs.GetBool(["--enabled=false"], "--enabled", true).Should().BeFalse();
        ((Action)(() => TradeSeedCliArgs.GetBool(["--enabled=yes"], "--enabled", false)))
            .Should().Throw<NgbArgumentInvalidException>();

        var fallbackDate = new DateOnly(2026, 8, 22);
        TradeSeedCliArgs.GetDateOnly([], "--date", fallbackDate).Should().Be(fallbackDate);
        TradeSeedCliArgs.GetDateOnly(["--date=2026-02-28"], "--date", fallbackDate)
            .Should().Be(new DateOnly(2026, 2, 28));
        ((Action)(() => TradeSeedCliArgs.GetDateOnly(["--date=not-a-date"], "--date", fallbackDate)))
            .Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void RequireConnectionString_CoversArgumentEnvironmentAndMissingConfiguration()
    {
        TradeSeedCliArgs.RequireConnectionString(["--connection=argument"]).Should().Be("argument");
        WithConnectionEnvironment("environment", () =>
            TradeSeedCliArgs.RequireConnectionString([]).Should().Be("environment"));
        WithConnectionEnvironment(null, () =>
            ((Action)(() => TradeSeedCliArgs.RequireConnectionString([])))
                .Should().Throw<NgbArgumentInvalidException>());
    }

    private static void WithConnectionEnvironment(string? value, Action action)
    {
        var previous = Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("NGB_CONNECTION_STRING", value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("NGB_CONNECTION_STRING", previous);
        }
    }
}
