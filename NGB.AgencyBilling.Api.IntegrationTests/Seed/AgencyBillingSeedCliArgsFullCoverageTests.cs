using FluentAssertions;
using NGB.AgencyBilling.Migrator.Seed;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Seed;

[CollectionDefinition(AgencyBillingEnvironmentCollection.Name, DisableParallelization = true)]
public sealed class AgencyBillingEnvironmentCollection
{
    public const string Name = "Agency Billing environment variable tests";
}

[Collection(AgencyBillingEnvironmentCollection.Name)]
public sealed class AgencyBillingSeedCliArgsFullCoverageTests
{
    [Fact]
    public void Parser_CoversEveryValueShapeAndFailure()
    {
        AgencyBillingSeedCliArgs.GetArgValue(["--COUNT", "7"], "--count").Should().Be("7");
        AgencyBillingSeedCliArgs.GetArgValue(["--count=8"], "--count").Should().Be("8");
        AgencyBillingSeedCliArgs.GetArgValue(["--count"], "--count").Should().BeNull();
        AgencyBillingSeedCliArgs.GetArgValue(["--other", "7"], "--count").Should().BeNull();

        AgencyBillingSeedCliArgs.GetInt([], "--count", 3).Should().Be(3);
        AgencyBillingSeedCliArgs.GetInt(["--count", "-7"], "--count", 3).Should().Be(-7);
        ((Action)(() => AgencyBillingSeedCliArgs.GetInt(["--count=invalid"], "--count", 3)))
            .Should().Throw<NgbArgumentInvalidException>();

        AgencyBillingSeedCliArgs.GetBool([], "--enabled", true).Should().BeTrue();
        AgencyBillingSeedCliArgs.GetBool(["--enabled=false"], "--enabled", true).Should().BeFalse();
        ((Action)(() => AgencyBillingSeedCliArgs.GetBool(["--enabled=yes"], "--enabled", false)))
            .Should().Throw<NgbArgumentInvalidException>();

        var fallbackDate = new DateOnly(2026, 8, 22);
        AgencyBillingSeedCliArgs.GetDateOnly([], "--date", fallbackDate).Should().Be(fallbackDate);
        AgencyBillingSeedCliArgs.GetDateOnly(["--date=2026-02-28"], "--date", fallbackDate)
            .Should().Be(new DateOnly(2026, 2, 28));
        ((Action)(() => AgencyBillingSeedCliArgs.GetDateOnly(["--date=not-a-date"], "--date", fallbackDate)))
            .Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void RequireConnectionString_CoversArgumentEnvironmentAndMissingConfiguration()
    {
        AgencyBillingSeedCliArgs.RequireConnectionString(["--connection=argument"]).Should().Be("argument");
        WithConnectionEnvironment("environment", () =>
            AgencyBillingSeedCliArgs.RequireConnectionString([]).Should().Be("environment"));
        WithConnectionEnvironment(null, () =>
            ((Action)(() => AgencyBillingSeedCliArgs.RequireConnectionString([])))
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
