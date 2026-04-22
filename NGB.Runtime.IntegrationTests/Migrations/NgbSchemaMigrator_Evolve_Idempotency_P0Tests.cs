using Dapper;
using FluentAssertions;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Migrations;

/// <summary>
/// P0: Evolve-based schema versioning must be idempotent.
/// The migrator must also be able to recreate the changelog table if it is missing.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NgbSchemaMigrator_Evolve_Idempotency_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task MigrateAsync_WhenChangelogIsMissing_RecreatesChangelog_AndIsIdempotent()
    {
        var packs = SchemaMigrator.DiscoverPacks(new[] { typeof(DatabaseBootstrapper).Assembly });

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // Simulate drift: metadata table is missing.
        await conn.ExecuteAsync("DROP TABLE IF EXISTS public.migration_changelog__platform;");

        await SchemaMigrator.MigrateAsync(
            Fixture.ConnectionString,
            packs,
            includePackIds: new[] { "platform" },
            repair: false,
            log: null);

        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT to_regclass('public.migration_changelog__platform') IS NOT NULL;");
        exists.Should().BeTrue();

        var count1 = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM public.migration_changelog__platform;");
        count1.Should().BeGreaterThan(0);

        // Second run must be a no-op (no extra changelog rows).
        await SchemaMigrator.MigrateAsync(
            Fixture.ConnectionString,
            packs,
            includePackIds: new[] { "platform" },
            repair: false,
            log: null);

        var count2 = await conn.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM public.migration_changelog__platform;");
        count2.Should().Be(count1);
    }
}
