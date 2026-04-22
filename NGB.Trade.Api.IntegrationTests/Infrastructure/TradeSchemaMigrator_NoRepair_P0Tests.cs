using FluentAssertions;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.Trade.PostgreSql.Bootstrap;
using NGB.Trade.PostgreSql.Migrations;
using Npgsql;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Infrastructure;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeSchemaMigrator_NoRepair_P0Tests(TradePostgresFixture fixture)
{
    [Fact]
    public async Task Migrate_WithoutRepair_Installs_Critical_Trade_Document_Guards_And_Indexes()
    {
        await fixture.ResetDatabaseAsync();
        await RecreatePublicSchemaAsync(fixture.ConnectionString);

        var packs = SchemaMigrator.DiscoverPacks(
        [
            typeof(DatabaseBootstrapper).Assembly,
            typeof(TradeMigrationPackContributor).Assembly
        ]);

        await SchemaMigrator.MigrateAsync(
            fixture.ConnectionString,
            packs,
            includePackIds: ["trade"],
            repair: false,
            dryRun: false,
            log: null);

        (await TriggerExistsAsync(fixture.ConnectionString, "doc_trd_purchase_receipt", "trg_posted_immutable"))
            .Should().BeTrue();
        (await TriggerExistsAsync(fixture.ConnectionString, "doc_trd_purchase_receipt__lines", "trg_posted_immutable"))
            .Should().BeTrue();
        (await TriggerExistsAsync(fixture.ConnectionString, "doc_trd_item_price_update__lines", "trg_posted_immutable"))
            .Should().BeTrue();
        (await IndexExistsAsync(fixture.ConnectionString, "doc_trd_item_price_update__lines", "ix_doc_trd_item_price_update__lines__currency"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Repair_Restores_Critical_Trade_Document_Indexes()
    {
        await fixture.ResetDatabaseAsync();
        await DropIndexAsync(fixture.ConnectionString, "ix_doc_trd_item_price_update__lines__currency");

        (await IndexExistsAsync(fixture.ConnectionString, "doc_trd_item_price_update__lines", "ix_doc_trd_item_price_update__lines__currency"))
            .Should().BeFalse();

        await TradeDatabaseBootstrapper.RepairModuleAsync(fixture.ConnectionString);

        (await IndexExistsAsync(fixture.ConnectionString, "doc_trd_item_price_update__lines", "ix_doc_trd_item_price_update__lines__currency"))
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

        var count = (int)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }

    private static async Task DropIndexAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"""
            DROP INDEX IF EXISTS public.{indexName};
            """,
            conn);

        await cmd.ExecuteNonQueryAsync();
    }
}
