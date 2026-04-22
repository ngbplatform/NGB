using Dapper;
using FluentAssertions;
using NGB.PostgreSql.Bootstrap;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseBootstrapper_ConcurrentInitialize_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task InitializeAsync_CalledConcurrently_OnFreshDatabase_DoesNotThrow_AndCreatesCoreObjects()
    {
        await Fixture.ResetDatabaseAsync();

        var dbName = $"ngb_it_bootstrap_{Guid.CreateVersion7():N}";
        await CreateDatabaseAsync(Fixture.ConnectionString, dbName);
        var cs = BuildDatabaseConnectionString(Fixture.ConnectionString, dbName);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            var tasks = Enumerable.Range(0, 8)
                .Select(_ => DatabaseBootstrapper.InitializeAsync(cs, cts.Token))
                .ToArray();

            await Task.WhenAll(tasks);

            await AssertCoreObjectsExistAsync(cs);
        }
        finally
        {
            await DropDatabaseAsync(Fixture.ConnectionString, dbName);
        }
    }

    private static async Task AssertCoreObjectsExistAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Tables are the minimum invariant; indexes are validated in separate tests.
        var tables = new[]
        {
            "accounting_accounts",
            "accounting_register_main",
            "accounting_turnovers",
            "accounting_balances",
            "accounting_closed_periods",
            "accounting_posting_state",
            "catalogs",
            "documents"
        };

        foreach (var t in tables)
        {
            var regclass = await conn.ExecuteScalarAsync<string?>(
                // Cast to text to avoid reading PG regclass (OID) as System.Object.
                "SELECT to_regclass('public.' || @t)::text;",
                new { t });

            regclass.Should().NotBeNullOrEmpty($"core table '{t}' must exist");
        }
    }

    private static string BuildDatabaseConnectionString(string baseConnectionString, string database)
    {
        var b = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = database
        };
        return b.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string baseConnectionString, string database)
    {
        var admin = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres"
        };

        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync();

        string sql = $"CREATE DATABASE \"{database}\";";
        await conn.ExecuteAsync(sql);
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string database)
    {
        var admin = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres"
        };

        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync();

        string sql = $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE);";
        await conn.ExecuteAsync(sql);
    }
}
