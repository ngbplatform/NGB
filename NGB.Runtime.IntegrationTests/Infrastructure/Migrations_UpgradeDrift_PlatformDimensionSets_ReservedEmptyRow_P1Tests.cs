using Dapper;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_PlatformDimensionSets_ReservedEmptyRow_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrations_ReinsertsGuidEmptyDimensionSetRow_IfRemovedByTruncate()
    {
        // Why this exists:
        // - Guid.Empty is a RESERVED dimension set id for the empty set.
        // - platform_dimension_set_items forbids Guid.Empty as a set id, but platform_dimension_sets must always
        //   contain the header row for Guid.Empty to satisfy FKs that reference it (and to allow safe joins).
        // - TRUNCATE bypasses row-level triggers (append-only guards), so the reserved row can be removed.

        await Fixture.ResetDatabaseAsync();

        // Arrange: remove *all* dimension sets including the reserved Guid.Empty row.
        await ExecuteAsync(
            Fixture.ConnectionString,
            "TRUNCATE TABLE public.platform_dimension_sets CASCADE;");

        // Sanity: reserved row is missing.
        (await DimensionSetRowExistsAsync(Fixture.ConnectionString, Guid.Empty)).Should().BeFalse();

        // Act: re-apply platform migrations.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: the reserved row is restored.
        (await DimensionSetRowExistsAsync(Fixture.ConnectionString, Guid.Empty)).Should().BeTrue();
    }

    private static async Task ExecuteAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);
        await conn.ExecuteAsync(sql);
    }

    private static async Task<bool> DimensionSetRowExistsAsync(string cs, Guid id)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var count = await conn.ExecuteScalarAsync<int>(
            "select count(*) from public.platform_dimension_sets where dimension_set_id = @id;",
            new { id });

        return count == 1;
    }
}
