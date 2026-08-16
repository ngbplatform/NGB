using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.CRM.Runtime;
using Npgsql;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Owns the immutable, representative CRM demo dataset used by reporting tests.
/// The expensive seed is created once per test run; every test gets an independent
/// DI scope backed by a connection string that rejects database writes.
/// </summary>
public sealed class CrmSeededReportingFixture : IAsyncLifetime
{
    private const int CompactGeneratedAccountCount = 10;
    private const int CompactGeneratedOpportunityCycleCount = 30;
    private const int ExpectedDemoDocumentCount = 194;
    private readonly CrmPostgresFixture _database = new();
    private IHost? _readOnlyHost;

    public IServiceProvider Services => _readOnlyHost?.Services
        ?? throw new InvalidOperationException("The seeded CRM reporting fixture is not initialized.");

    public string ReadOnlyConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        using (var seedHost = CrmHostFactory.Create(_database.ConnectionString, ConfigureCompactSeed))
        await using (var seedScope = seedHost.Services.CreateAsyncScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<ICrmDemoSeedService>();
            var result = await seed.EnsureDemoAsync(CancellationToken.None);

            if (result.DocumentsCreated != ExpectedDemoDocumentCount)
            {
                throw new InvalidOperationException(
                    $"CRM reporting baseline is incomplete: expected {ExpectedDemoDocumentCount} documents, " +
                    $"created {result.DocumentsCreated}.");
            }
        }

        ReadOnlyConnectionString = BuildReadOnlyConnectionString(_database.ConnectionString);
        _readOnlyHost = CrmHostFactory.Create(ReadOnlyConnectionString, ConfigureCompactSeed);
    }

    public async Task DisposeAsync()
    {
        try
        {
            _readOnlyHost?.Dispose();

            if (!string.IsNullOrWhiteSpace(ReadOnlyConnectionString))
            {
                using var connection = new NpgsqlConnection(ReadOnlyConnectionString);
                NpgsqlConnection.ClearPool(connection);
            }
        }
        finally
        {
            await _database.DisposeAsync();
        }
    }

    private static string BuildReadOnlyConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "NGB CRM reporting integration tests"
        };

        const string readOnlyOption = "-c default_transaction_read_only=on";
        builder.Options = string.IsNullOrWhiteSpace(builder.Options)
            ? readOnlyOption
            : $"{builder.Options.Trim()} {readOnlyOption}";

        return builder.ConnectionString;
    }

    private static void ConfigureCompactSeed(IServiceCollection services)
    {
        services.RemoveAll<CrmDemoSeedOptions>();
        services.AddSingleton(new CrmDemoSeedOptions
        {
            GeneratedAccountCount = CompactGeneratedAccountCount,
            GeneratedOpportunityCycleCount = CompactGeneratedOpportunityCycleCount
        });
    }
}
