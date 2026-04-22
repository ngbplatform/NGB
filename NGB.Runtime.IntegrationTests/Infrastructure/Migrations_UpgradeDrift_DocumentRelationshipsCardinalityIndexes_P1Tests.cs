using Dapper;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_DocumentRelationshipsCardinalityIndexes_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrations_RecreatesDocumentRelationshipCardinalityIndexes_WhenDropped()
    {
        // Our migration runner is "CREATE IF NOT EXISTS" style.
        // This drift test verifies that dropping document relationship cardinality indexes is recoverable.

        var indexes = new[]
        {
            "ux_docrel_from_rev_of",
            "ux_docrel_from_created_from",
            "ux_docrel_from_supersedes",
            "ux_docrel_to_supersedes"
        };

        foreach (var idx in indexes)
            await DropIndexIfExistsAsync(Fixture.ConnectionString, idx);

        foreach (var idx in indexes)
            (await IndexExistsAsync(Fixture.ConnectionString, idx)).Should().BeFalse();

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        foreach (var idx in indexes)
            (await IndexExistsAsync(Fixture.ConnectionString, idx)).Should().BeTrue();
    }

    private static async Task DropIndexIfExistsAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);
        await conn.ExecuteAsync($"DROP INDEX IF EXISTS {indexName};");
    }

    private static async Task<bool> IndexExistsAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public'
                  AND c.relkind = 'i'
                  AND c.relname = @name
            ) THEN 1 ELSE 0 END;
            """,
            new { name = indexName });

        return exists == 1;
    }
}
