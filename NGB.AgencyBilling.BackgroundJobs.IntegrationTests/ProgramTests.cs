using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.BackgroundJobs.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace NGB.AgencyBilling.BackgroundJobs.IntegrationTests;

[CollectionDefinition(PostgreSqlCollection.Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "Agency Billing background jobs PostgreSQL";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("ngb_agency_billing_background_jobs_program_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();

        ConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Options = "-c TimeZone=UTC",
            Pooling = true,
            MaxPoolSize = 16
        }.ToString();
    }

    public async Task DisposeAsync()
    {
        if (_container is null)
            return;

        NpgsqlConnection.ClearAllPools();
        await _container.DisposeAsync();
    }
}

[Collection(PostgreSqlCollection.Name)]
public sealed class ProgramTests(PostgreSqlFixture fixture)
{
    [Fact]
    public void Program_StartsWithExpectedDashboardTitle()
    {
        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] = fixture.ConnectionString,
            ["ConnectionStrings__Hangfire"] = fixture.ConnectionString,
            ["KeycloakSettings__Issuer"] = "https://example.invalid/realms/ngb",
            ["KeycloakSettings__RequireHttpsMetadata"] = bool.FalseString,
            ["KeycloakSettings__ClientIds__0"] = "ngb-admin-console",
            ["BackgroundJobs__Enabled"] = bool.FalseString,
            ["Serilog__WriteTo__1__Args__serverUrl"] = "http://localhost:5341"
        });
        using var factory = new WebApplicationFactory<Program>();

        factory.Services.GetRequiredService<IOptions<BackgroundJobsHostingOptions>>().Value.DashboardTitle
            .Should().Be("NGB: Agency Billing - Background Jobs");
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
