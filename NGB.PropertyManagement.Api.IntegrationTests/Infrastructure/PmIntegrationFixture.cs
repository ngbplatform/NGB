using NGB.PostgreSql.Dapper;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

public sealed class PmIntegrationFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private Respawner? _respawner;
    private PmKeycloakFixture? _keycloak;

    private static readonly SemaphoreSlim ResetSemaphore = new(1, 1);

    public string ConnectionString { get; private set; } = string.Empty;

    public PmKeycloakFixture Keycloak => _keycloak
        ?? throw new NotSupportedException("Keycloak fixture is not initialized.");

    public async Task InitializeAsync()
    {
        // Some tests use raw Dapper operations (including DateOnly parameters) without building DI.
        // Ensure Dapper type handlers are registered once per test process.
        DapperTypeHandlers.Register();

        _keycloak = new PmKeycloakFixture();

        _container = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("ngb_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await Task.WhenAll(
            _container.StartAsync(),
            _keycloak.InitializeAsync());

        // Ensure predictable UTC semantics for all tests, including raw NpgsqlConnection usage.
        // We set the PostgreSQL session TimeZone at startup using libpq options.
        var csb = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Options = "-c TimeZone=UTC",
            Pooling = false
        };
        ConnectionString = csb.ToString();

        await PmMigrationSet.ApplyPlatformAndPmMigrationsAsync(ConnectionString);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            // We don't exclude any tables: migrations are idempotent (CREATE IF NOT EXISTS),
            // so we can safely re-run migrations after reset.
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
                // If Respawn cached a table that was dropped, recreate schema and rebuild.
                await RecreatePublicSchemaAsync(conn);
                rebuildRespawner = true;
            }

            await PmMigrationSet.ApplyPlatformAndPmMigrationsAsync(ConnectionString);

            if (rebuildRespawner)
            {
                _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
                {
                    DbAdapter = DbAdapter.Postgres,
                    SchemasToInclude = ["public"],
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
        if (_keycloak is not null)
            await _keycloak.DisposeAsync();

        if (_container is not null)
            await _container.DisposeAsync();
    }
}
