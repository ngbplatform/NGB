using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.Metadata.Schema;
using NGB.PostgreSql.Schema;
using NGB.PostgreSql.UnitOfWork;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class PlatformSchema_DetectsMissingCriticalIndex_P4_4_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DbSchemaInspectorSnapshot_DetectsMissingCoreIndex_AndMigrationsRestoreIt()
    {
        // IMPORTANT:
        // Core platform migrations are "CREATE IF NOT EXISTS" style.
        // Dropped indexes *should* be restored by re-applying migrations.

        string dbName = $"ngb_idx_mismatch_{Guid.CreateVersion7():N}";
        string tempConnStr = BuildDatabaseConnectionString(Fixture.ConnectionString, dbName);

        await CreateDatabaseAsync(Fixture.ConnectionString, dbName);

        try
        {
            await MigrationSet.ApplyPlatformMigrationsAsync(tempConnStr);

            // Fault-injection: drop a critical non-PK index.
            await DropIndexAsync(tempConnStr, "ix_acc_reg_period_month");

            var snapshot = await GetSnapshotAsync(tempConnStr);

            snapshot.IndexesByTable.Should().ContainKey("accounting_register_main");
            snapshot.IndexesByTable["accounting_register_main"].Select(i => i.IndexName)
                .Should().NotContain("ix_acc_reg_period_month");

            // Act: re-apply migrations (should recreate the index).
            await MigrationSet.ApplyPlatformMigrationsAsync(tempConnStr);

            snapshot = await GetSnapshotAsync(tempConnStr);
            snapshot.IndexesByTable["accounting_register_main"].Select(i => i.IndexName)
                .Should().Contain("ix_acc_reg_period_month");
        }
        finally
        {
            await DropDatabaseAsync(Fixture.ConnectionString, dbName);
        }
    }

    private static async Task DropIndexAsync(string connectionString, string indexName)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync($"DROP INDEX IF EXISTS {indexName};");
    }

    private static async Task<DbSchemaSnapshot> GetSnapshotAsync(string connectionString)
    {
        await using var uow = new PostgresUnitOfWork(connectionString, NullLogger<PostgresUnitOfWork>.Instance);
        var inspector = new PostgresSchemaInspector(uow);
        return await inspector.GetSnapshotAsync(CancellationToken.None);
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
