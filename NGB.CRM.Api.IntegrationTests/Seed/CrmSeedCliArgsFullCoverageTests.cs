using FluentAssertions;
using NGB.CRM.Migrator.Seed;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Seed;

[CollectionDefinition(EnvironmentCollection.Name, DisableParallelization = true)]
public sealed class EnvironmentCollection
{
    public const string Name = "CRM environment variable tests";
}

[Collection(EnvironmentCollection.Name)]
public sealed class CrmSeedCliArgsFullCoverageTests
{
    [Fact]
    public void GetArgValue_CoversSeparateInlineTrailingAndUnknownForms()
    {
        CrmSeedCliArgs.GetArgValue(["--CONNECTION", "separate"], "--connection").Should().Be("separate");
        CrmSeedCliArgs.GetArgValue(["--connection=inline"], "--connection").Should().Be("inline");
        CrmSeedCliArgs.GetArgValue(["--connection"], "--connection").Should().BeNull();
        CrmSeedCliArgs.GetArgValue(["--other", "value"], "--connection").Should().BeNull();
    }

    [Fact]
    public void RequireConnectionString_CoversArgumentEnvironmentAndMissingConfiguration()
    {
        CrmSeedCliArgs.RequireConnectionString(["--connection=argument"]).Should().Be("argument");

        WithConnectionEnvironment("environment", () =>
            CrmSeedCliArgs.RequireConnectionString([]).Should().Be("environment"));
        WithConnectionEnvironment(null, () =>
            ((Action)(() => CrmSeedCliArgs.RequireConnectionString([])))
                .Should().Throw<NgbConfigurationViolationException>());
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
