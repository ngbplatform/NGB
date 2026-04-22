using NGB.PostgreSql.Dapper;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Infrastructure;

public sealed class TradePostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private Respawner? _respawner;

    private static readonly SemaphoreSlim ResetSemaphore = new(1, 1);

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        DapperTypeHandlers.Register();

        _container = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("ngb_trade_tests")
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

        await TradeMigrationSet.ApplyPlatformAndTradeMigrationsAsync(ConnectionString);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"]
        });
    }

    public async Task ResetDatabaseAsync()
    {
        if (_respawner is null)
            throw new NotSupportedException("Fixture is not initialized.");

        await ResetSemaphore.WaitAsync();
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            var rebuildRespawner = false;

            try
            {
                await _respawner.ResetAsync(conn);
            }
            catch (PostgresException ex) when (ex.SqlState is "42P01" or "3F000")
            {
                await RecreatePublicSchemaAsync(conn);
                rebuildRespawner = true;
            }

            await TradeMigrationSet.ApplyPlatformAndTradeMigrationsAsync(ConnectionString);

            if (rebuildRespawner)
            {
                _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
                {
                    DbAdapter = DbAdapter.Postgres,
                    SchemasToInclude = ["public"]
                });
            }
        }
        finally
        {
            ResetSemaphore.Release();
        }
    }

    private static async Task RecreatePublicSchemaAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand(
            """
            DROP SCHEMA IF EXISTS public CASCADE;
            CREATE SCHEMA public;
            GRANT ALL ON SCHEMA public TO public;
            """,
            conn);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
