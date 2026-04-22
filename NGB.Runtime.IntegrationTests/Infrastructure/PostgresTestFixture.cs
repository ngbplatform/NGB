using NGB.PostgreSql.Dapper;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public sealed class PostgresTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private Respawner? _respawner;

    private static readonly SemaphoreSlim ResetSemaphore = new(1, 1);

    /// <summary>
    /// The last initialized integration-test connection string.
    /// Used by some helper APIs (e.g., CreateScope()) to reduce boilerplate.
    /// </summary>
    internal static string CurrentConnectionString { get; private set; } = string.Empty;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        // Some tests use raw Dapper operations (including DateOnly parameters) without building DI.
        // Ensure Dapper type handlers are registered once per test process.
        DapperTypeHandlers.Register();

        // Arrange
        // NOTE: Prefer the Debian-based image in tests.
        // We observed sporadic postmaster exits under heavy DDL churn on alpine/musl (drift-repair suites).
        _container = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("ngb_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        // Act
        await _container.StartAsync();
        // Ensure predictable UTC semantics for all tests, including raw NpgsqlConnection usage.
        // We set the PostgreSQL session TimeZone at startup using libpq options.
        var csb = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Options = "-c TimeZone=UTC",
            Pooling = false
        };
        ConnectionString = csb.ToString();
        CurrentConnectionString = ConnectionString;

        // Create schema once and prepare Respawn.
        await MigrationSet.ApplyPlatformMigrationsAsync(ConnectionString);

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
                // Some drift-repair tests intentionally DROP TABLE / DROP SCHEMA objects.
                // Respawn caches the table set at fixture initialization; if a referenced table is missing,
                // ResetAsync fails with "relation does not exist".
                // For test isolation, recreate the public schema from scratch and rebuild Respawn metadata.
                await RecreatePublicSchemaAsync(conn);
                rebuildRespawner = true;
            }

            // Re-apply platform migrations to ensure schema is present (idempotent drift-repair included).
            await MigrationSet.ApplyPlatformMigrationsAsync(ConnectionString);

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
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
