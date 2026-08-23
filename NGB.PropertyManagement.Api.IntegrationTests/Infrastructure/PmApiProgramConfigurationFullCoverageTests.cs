using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PmApiProgramEnvironmentCollection
{
    public const string Name = "Property Management API Program environment";
}

[Collection(PmApiProgramEnvironmentCollection.Name)]
public sealed class PmApiProgramConfigurationFullCoverageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Program_WithoutUsableConnectionString_RejectsConfiguration(string? connectionString)
    {
        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["ConnectionStrings__DefaultConnection"] = connectionString
        });
        using var factory = new WebApplicationFactory<Program>();

        var act = () => _ = factory.Services;

        act.Should().Throw<Exception>()
            .Where(exception => FindException(exception, "Please provide PostgreSQL connection string"));
    }

    [Fact]
    public void Program_WithValidConnectionString_InProduction_BuildsWithoutDevelopmentMiddleware()
    {
        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["ConnectionStrings__DefaultConnection"] =
                "Host=localhost;Port=5432;Database=ngb_coverage;Username=ngb;Password=ngb",
            ["KeycloakSettings__Issuer"] = "https://identity.example.test/realms/ngb",
            ["KeycloakSettings__ClientIds__0"] = "ngb-pm-api",
            ["ExternalLinksSettings__HealthUiUrl"] = "https://health.example.test",
            ["ExternalLinksSettings__BackgroundJobsUiUrl"] = "https://jobs.example.test",
            ["Serilog__WriteTo__1__Args__serverUrl"] = "http://localhost:5341"
        });
        using var factory = new WebApplicationFactory<Program>();

        factory.Services.Should().NotBeNull();
    }

    private static bool FindException(Exception exception, string messageFragment)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(messageFragment, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var pair in values)
            {
                _previousValues[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        public void Dispose()
        {
            foreach (var pair in _previousValues)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
