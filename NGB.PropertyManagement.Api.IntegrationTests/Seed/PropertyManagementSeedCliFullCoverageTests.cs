using FluentAssertions;
using NGB.PropertyManagement.Migrator.Seed;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Seed;

[CollectionDefinition(PropertyManagementEnvironmentCollection.Name, DisableParallelization = true)]
public sealed class PropertyManagementEnvironmentCollection
{
    public const string Name = "Property Management environment variable tests";
}

[Collection(PropertyManagementEnvironmentCollection.Name)]
public sealed class PropertyManagementSeedCliFullCoverageTests
{
    [Fact]
    public void DemoParser_CoversEveryValueShapeAndFailure()
    {
        PropertyManagementSeedCliArgs.GetArgValue(["--COUNT", "7"], "--count").Should().Be("7");
        PropertyManagementSeedCliArgs.GetArgValue(["--count=8"], "--count").Should().Be("8");
        PropertyManagementSeedCliArgs.GetArgValue(["--count"], "--count").Should().BeNull();
        PropertyManagementSeedCliArgs.GetArgValue(["--other", "7"], "--count").Should().BeNull();

        PropertyManagementSeedCliArgs.GetInt([], "--count", 3).Should().Be(3);
        PropertyManagementSeedCliArgs.GetInt(["--count", "-7"], "--count", 3).Should().Be(-7);
        ((Action)(() => PropertyManagementSeedCliArgs.GetInt(["--count=invalid"], "--count", 3)))
            .Should().Throw<NgbArgumentInvalidException>();

        PropertyManagementSeedCliArgs.GetBool([], "--enabled", true).Should().BeTrue();
        PropertyManagementSeedCliArgs.GetBool(["--enabled=false"], "--enabled", true).Should().BeFalse();
        ((Action)(() => PropertyManagementSeedCliArgs.GetBool(["--enabled=yes"], "--enabled", false)))
            .Should().Throw<NgbArgumentInvalidException>();

        PropertyManagementSeedCliArgs.GetDouble([], "--ratio", 1.5).Should().Be(1.5);
        PropertyManagementSeedCliArgs.GetDouble(["--ratio=1,234.5"], "--ratio", 0).Should().Be(1234.5);
        ((Action)(() => PropertyManagementSeedCliArgs.GetDouble(["--ratio=invalid"], "--ratio", 0)))
            .Should().Throw<NgbArgumentInvalidException>();

        PropertyManagementSeedCliArgs.GetString([], "--name", "fallback").Should().Be("fallback");
        PropertyManagementSeedCliArgs.GetString(["--name=  Demo  "], "--name", "fallback").Should().Be("Demo");

        var fallbackDate = new DateOnly(2026, 8, 22);
        PropertyManagementSeedCliArgs.GetDateOnly([], "--date", fallbackDate).Should().Be(fallbackDate);
        PropertyManagementSeedCliArgs.GetDateOnly(["--date=2026-02-28"], "--date", fallbackDate)
            .Should().Be(new DateOnly(2026, 2, 28));
        ((Action)(() => PropertyManagementSeedCliArgs.GetDateOnly(["--date=not-a-date"], "--date", fallbackDate)))
            .Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task DefaultsCommandAndConnectionParsing_CoverAllBoundaries()
    {
        PropertyManagementSeedDefaultsCli.IsSeedDefaultsCommand([]).Should().BeFalse();
        PropertyManagementSeedDefaultsCli.IsSeedDefaultsCommand(["other"]).Should().BeFalse();
        PropertyManagementSeedDefaultsCli.IsSeedDefaultsCommand(["SEED-DEFAULTS"]).Should().BeTrue();
        PropertyManagementSeedDefaultsCli.TrimCommand([]).Should().BeEmpty();
        PropertyManagementSeedDefaultsCli.TrimCommand(["seed-defaults"]).Should().BeEmpty();
        PropertyManagementSeedDefaultsCli.TrimCommand(["seed-defaults", "--one", "two"])
            .Should().Equal("--one", "two");

        PropertyManagementSeedCliArgs.RequireConnectionString(["--connection=argument"]).Should().Be("argument");
        WithConnectionEnvironment("environment", () =>
            PropertyManagementSeedCliArgs.RequireConnectionString([]).Should().Be("environment"));
        WithConnectionEnvironment(null, () =>
            ((Action)(() => PropertyManagementSeedCliArgs.RequireConnectionString([])))
                .Should().Throw<NgbArgumentInvalidException>());

        await WithConnectionEnvironmentAsync(null, async () =>
        {
            (await PropertyManagementSeedDefaultsCli.RunAsync(["--other"])).Should().Be(2);
            (await PropertyManagementSeedDefaultsCli.RunAsync(["--connection"])).Should().Be(2);
            (await PropertyManagementSeedDefaultsCli.RunAsync(["--connection", ""])).Should().Be(2);
            (await PropertyManagementSeedDefaultsCli.RunAsync(["--connection=not-a-connection-string"])).Should().Be(1);
        });
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

    private static async Task WithConnectionEnvironmentAsync(string? value, Func<Task> action)
    {
        var previous = Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("NGB_CONNECTION_STRING", value);
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("NGB_CONNECTION_STRING", previous);
        }
    }
}
