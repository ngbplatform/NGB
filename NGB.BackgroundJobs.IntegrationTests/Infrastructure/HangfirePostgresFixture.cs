using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Dapper;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Infrastructure;

public sealed class HangfirePostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public JobStorage JobStorage { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Some tests use raw Dapper operations before the DI composition root is built.
        // Ensure Dapper understands DateOnly, etc. (idempotent).
        DapperTypeHandlers.Register();

        _container = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("ngb_bgjob_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();

        var csb = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Options = "-c TimeZone=UTC",
            Pooling = false
        };

        ConnectionString = csb.ToString();

        // Ensure platform schema exists (idempotent). Some jobs run direct SQL against platform tables.
        await DatabaseBootstrapper.InitializeAsync(ConnectionString);

        // Integration tests intentionally simulate drift by dropping guards/indexes.
        // Keep the baseline deterministic by running an explicit drift-repair pass.
        await DatabaseBootstrapper.RepairAsync(ConnectionString);

        var storageOptions = new PostgreSqlStorageOptions
        {
            PrepareSchemaIfNecessary = true
        };

        JobStorage = new PostgreSqlStorage(
            new NpgsqlConnectionFactory(ConnectionString, storageOptions, _ => { }),
            storageOptions);

        // Force schema creation/upgrade by touching the storage once.
        using var conn = JobStorage.GetConnection();
        using var l = conn.AcquireDistributedLock("ngb:bgjob:schema:init", TimeSpan.FromSeconds(5));
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
