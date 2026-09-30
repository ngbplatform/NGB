using NGB.Testing.Containers;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace NGB.Migrator.Core.IntegrationTests.Infrastructure;

public sealed class MigratorPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("ngb_migrator_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await using (var startupLease = await TestcontainerStartupGate.AcquireAsync())
            await _container.StartAsync();

        var csb = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Options = "-c TimeZone=UTC",
            Pooling = true,
            MaxPoolSize = 16,
            NoResetOnClose = false
        };

        ConnectionString = csb.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            NpgsqlConnection.ClearPool(connection);
        }

        if (_container is not null)
            await _container.DisposeAsync();
    }
}
