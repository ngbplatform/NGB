using FluentAssertions;
using NGB.CRM.PostgreSql.Bootstrap;
using NGB.CRM.PostgreSql.Migrations;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;
using Npgsql;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

[Collection(CrmPostgresCollection.Name)]
public sealed class CrmSchemaMigrator_NoRepair_P0Tests(CrmPostgresFixture fixture)
{
    [Fact]
    public async Task Migrate_WithoutRepair_Installs_Critical_Crm_Tables_Views_And_Guards()
    {
        await fixture.ResetDatabaseAsync();
        await RecreatePublicSchemaAsync(fixture.ConnectionString);

        var packs = SchemaMigrator.DiscoverPacks(
        [
            typeof(DatabaseBootstrapper).Assembly,
            typeof(CrmMigrationPackContributor).Assembly
        ]);

        await SchemaMigrator.MigrateAsync(
            fixture.ConnectionString,
            packs,
            includePackIds: ["crm"],
            repair: false,
            dryRun: false,
            log: null);

        (await RelationExistsAsync(fixture.ConnectionString, "cat_crm_account", "r")).Should().BeTrue();
        (await RelationExistsAsync(fixture.ConnectionString, "doc_crm_quote__lines", "r")).Should().BeTrue();
        (await RelationExistsAsync(fixture.ConnectionString, "crm_opportunities_current", "v")).Should().BeTrue();
        (await TriggerExistsAsync(fixture.ConnectionString, "doc_crm_quote", "trg_posted_immutable")).Should().BeTrue();
        (await TriggerExistsAsync(fixture.ConnectionString, "doc_crm_quote__lines", "trg_posted_immutable")).Should().BeTrue();
        (await IndexExistsAsync(fixture.ConnectionString, "doc_crm_quote__lines", "ix_doc_crm_quote__lines__product_id")).Should().BeTrue();
    }

    [Fact]
    public async Task Repair_Restores_Critical_Crm_Indexes()
    {
        await fixture.ResetDatabaseAsync();
        await DropIndexAsync(fixture.ConnectionString, "ix_doc_crm_quote__lines__product_id");

        (await IndexExistsAsync(fixture.ConnectionString, "doc_crm_quote__lines", "ix_doc_crm_quote__lines__product_id")).Should().BeFalse();

        await CrmDatabaseBootstrapper.RepairModuleAsync(fixture.ConnectionString);

        (await IndexExistsAsync(fixture.ConnectionString, "doc_crm_quote__lines", "ix_doc_crm_quote__lines__product_id")).Should().BeTrue();
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

    private static async Task<bool> RelationExistsAsync(string cs, string relationName, string relationKind)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*)::int
            FROM pg_class c
            JOIN pg_namespace ns ON ns.oid = c.relnamespace
            WHERE ns.nspname = 'public'
              AND c.relname = @relation
              AND c.relkind = @kind;
            """,
            conn);

        cmd.Parameters.AddWithValue("relation", relationName);
        cmd.Parameters.AddWithValue("kind", relationKind);

        return (int)(await cmd.ExecuteScalarAsync())! > 0;
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

        return (int)(await cmd.ExecuteScalarAsync())! > 0;
    }

    private static async Task<bool> IndexExistsAsync(string cs, string tableName, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*)::int
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = @table
              AND indexname = @index;
            """,
            conn);

        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("index", indexName);

        return (int)(await cmd.ExecuteScalarAsync())! > 0;
    }

    private static async Task DropIndexAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP INDEX IF EXISTS public.{indexName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
