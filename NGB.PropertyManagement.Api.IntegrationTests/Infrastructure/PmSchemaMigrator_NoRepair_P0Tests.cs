using FluentAssertions;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.PropertyManagement.PostgreSql.Migrations;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmSchemaMigrator_NoRepair_P0Tests(PmIntegrationFixture fixture)
{
    [Fact]
    public async Task Migrate_WithoutRepair_Installs_TrgPostedImmutable_ForPmTypedDocumentTables()
    {
        await fixture.ResetDatabaseAsync();
        await RecreatePublicSchemaAsync(fixture.ConnectionString);

        var packs = SchemaMigrator.DiscoverPacks(
        [
            typeof(DatabaseBootstrapper).Assembly,
            typeof(PropertyManagementMigrationPackContributor).Assembly
        ]);

        await SchemaMigrator.MigrateAsync(
            fixture.ConnectionString,
            packs,
            includePackIds: ["pm"],
            repair: false,
            dryRun: false,
            log: null);

        (await TriggerExistsAsync(fixture.ConnectionString, "doc_pm_rent_charge", "trg_posted_immutable"))
            .Should().BeTrue();
        (await TriggerExistsAsync(fixture.ConnectionString, "doc_pm_lease__parties", "trg_posted_immutable"))
            .Should().BeTrue();
        (await TriggerExistsAsync(fixture.ConnectionString, "doc_pm_work_order_completion", "trg_posted_immutable"))
            .Should().BeTrue();
    }

    private static async Task RecreatePublicSchemaAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            DROP SCHEMA IF EXISTS public CASCADE;
            CREATE SCHEMA public;
            GRANT ALL ON SCHEMA public TO public;
            """,
            conn);

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TriggerExistsAsync(string cs, string tableName, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*)::int
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace ns ON ns.oid = c.relnamespace
            WHERE ns.nspname = 'public'
              AND c.relname = @table
              AND t.tgname = @trigger;
            """,
            conn);

        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("trigger", triggerName);

        var count = (int)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }
}
