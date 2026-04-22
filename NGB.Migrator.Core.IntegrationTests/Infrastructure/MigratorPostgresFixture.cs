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

        await _container.StartAsync();

        var csb = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Options = "-c TimeZone=UTC",
            Pooling = false
        };

        ConnectionString = csb.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
