using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CrmApiProgramEnvironmentCollection
{
    public const string Name = "CRM API Program environment";
}

[Collection(CrmApiProgramEnvironmentCollection.Name)]
public sealed class CrmApiProgramFullCoverageTests
{
    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task Program_WithValidConfiguration_StartsAndMapsEnvironmentSpecificSwagger(
        string environmentName,
        HttpStatusCode expectedStatusCode)
    {
        using var environment = new EnvironmentVariableScope(ValidConfiguration(environmentName));
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(expectedStatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Program_WithoutUsableConnectionString_RejectsConfiguration(string? connectionString)
    {
        var configuration = ValidConfiguration("Production");
        configuration["ConnectionStrings__DefaultConnection"] = connectionString;
        using var environment = new EnvironmentVariableScope(configuration);
        using var factory = new WebApplicationFactory<Program>();

        var act = () => _ = factory.Services;

        act.Should().Throw<Exception>()
            .Where(exception => FindException(exception, "Please provide PostgreSQL connection string"));
    }

    private static Dictionary<string, string?> ValidConfiguration(string environmentName) => new(StringComparer.Ordinal)
    {
        ["ASPNETCORE_ENVIRONMENT"] = environmentName,
        ["DOTNET_ENVIRONMENT"] = environmentName,
        ["ConnectionStrings__DefaultConnection"] =
            "Host=127.0.0.1;Port=1;Database=ngb_program_test;Username=postgres;Password=postgres;Timeout=1;Command Timeout=1;Pooling=false",
        ["KeycloakSettings__Issuer"] = "https://example.invalid/realms/ngb",
        ["KeycloakSettings__RequireHttpsMetadata"] = bool.FalseString,
        ["KeycloakSettings__ClientIds__0"] = "ngb-api-tests",
        ["ExternalLinksSettings__HealthUiUrl"] = "https://example.invalid/health-ui",
        ["ExternalLinksSettings__BackgroundJobsUiUrl"] = "https://example.invalid/hangfire",
        ["Serilog__WriteTo__1__Args__serverUrl"] = "http://127.0.0.1:5341"
    };

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
