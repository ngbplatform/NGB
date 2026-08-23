using FluentAssertions;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.Persistence.Migrations;
using NGB.PropertyManagement.PostgreSql.Bootstrap;
using NGB.PropertyManagement.PostgreSql.Migrations;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

[Collection(PmSchemaIntegrationCollection.Name)]
public sealed class PmSchemaMigrator_NoRepair_P0Tests(PmSchemaIntegrationFixture fixture)
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

    [Fact]
    public async Task Repair_WithoutExplicitOptions_CompletesAgainstMigratedSchema()
    {
        await fixture.ResetDatabaseAsync();

        var act = () => PropertyManagementDatabaseBootstrapper.RepairModuleAsync(fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(60_000)]
    public async Task Repair_WithExplicitTimeoutBoundaries_CompletesAgainstMigratedSchema(int milliseconds)
    {
        await fixture.ResetDatabaseAsync();
        var options = new MigrationExecutionOptions(
            LockTimeout: TimeSpan.FromMilliseconds(milliseconds),
            StatementTimeout: TimeSpan.FromMilliseconds(milliseconds));

        var act = () => PropertyManagementDatabaseBootstrapper.RepairModuleAsync(
            fixture.ConnectionString,
            options,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
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
