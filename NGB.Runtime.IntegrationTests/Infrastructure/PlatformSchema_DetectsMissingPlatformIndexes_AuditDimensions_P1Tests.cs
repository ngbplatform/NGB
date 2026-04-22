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
public sealed class PlatformSchema_DetectsMissingPlatformIndexes_AuditDimensions_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DbSchemaInspectorSnapshot_DetectsMissingAuditPagingIndex_AndMigrationsRestoreIt()
    {
        // This complements Accounting-focused drift tests by covering the platform AuditLog paging indexes.
        string dbName = $"ngb_idx_mismatch_audit_{Guid.CreateVersion7():N}";
        string tempConnStr = BuildDatabaseConnectionString(Fixture.ConnectionString, dbName);

        await CreateDatabaseAsync(Fixture.ConnectionString, dbName);

        try
        {
            await MigrationSet.ApplyPlatformMigrationsAsync(tempConnStr);

            // Fault-injection: drop a critical cursor/paging index.
            await DropIndexAsync(tempConnStr, "ix_platform_audit_events_occurred_at_id_desc");

            var snapshot = await GetSnapshotAsync(tempConnStr);
            snapshot.IndexesByTable.Should().ContainKey("platform_audit_events");
            snapshot.IndexesByTable["platform_audit_events"].Select(i => i.IndexName)
                .Should().NotContain("ix_platform_audit_events_occurred_at_id_desc");

            // Act: re-apply migrations (should recreate the index).
            await MigrationSet.ApplyPlatformMigrationsAsync(tempConnStr);

            snapshot = await GetSnapshotAsync(tempConnStr);
            snapshot.IndexesByTable["platform_audit_events"].Select(i => i.IndexName)
                .Should().Contain("ix_platform_audit_events_occurred_at_id_desc");
        }
        finally
        {
            await DropDatabaseAsync(Fixture.ConnectionString, dbName);
        }
    }

    [Fact]
    public async Task DbSchemaInspectorSnapshot_DetectsMissingDimensionsUniqueIndex_AndMigrationsRestoreIt()
    {
        // Dimensions are a core platform primitive; the unique code_norm index is critical for correctness.
        string dbName = $"ngb_idx_mismatch_dim_{Guid.CreateVersion7():N}";
        string tempConnStr = BuildDatabaseConnectionString(Fixture.ConnectionString, dbName);

        await CreateDatabaseAsync(Fixture.ConnectionString, dbName);

        try
        {
            await MigrationSet.ApplyPlatformMigrationsAsync(tempConnStr);

            await DropIndexAsync(tempConnStr, "ux_platform_dimensions_code_norm_not_deleted");

            var snapshot = await GetSnapshotAsync(tempConnStr);
            snapshot.IndexesByTable.Should().ContainKey("platform_dimensions");
            snapshot.IndexesByTable["platform_dimensions"].Select(i => i.IndexName)
                .Should().NotContain("ux_platform_dimensions_code_norm_not_deleted");

            await MigrationSet.ApplyPlatformMigrationsAsync(tempConnStr);

            snapshot = await GetSnapshotAsync(tempConnStr);
            snapshot.IndexesByTable["platform_dimensions"].Select(i => i.IndexName)
                .Should().Contain("ux_platform_dimensions_code_norm_not_deleted");
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
