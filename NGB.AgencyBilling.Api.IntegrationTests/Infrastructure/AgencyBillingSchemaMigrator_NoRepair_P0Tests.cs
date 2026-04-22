using FluentAssertions;
using NGB.AgencyBilling.PostgreSql.Migrations;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;
using Npgsql;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;

[Collection(AgencyBillingPostgresCollection.Name)]
public sealed class AgencyBillingSchemaMigrator_NoRepair_P0Tests(AgencyBillingPostgresFixture fixture)
{
    [Fact]
    public async Task Migrate_WithoutRepair_Installs_Critical_AgencyBilling_Tables()
    {
        await fixture.ResetDatabaseAsync();
        await RecreatePublicSchemaAsync(fixture.ConnectionString);

        var packs = SchemaMigrator.DiscoverPacks(
        [
            typeof(DatabaseBootstrapper).Assembly,
            typeof(AgencyBillingMigrationPackContributor).Assembly
        ]);

        await SchemaMigrator.MigrateAsync(
            fixture.ConnectionString,
            packs,
            includePackIds: ["agency-billing"],
            repair: false,
            dryRun: false,
            log: null);

        (await TableExistsAsync(fixture.ConnectionString, "cat_ab_client")).Should().BeTrue();
        (await TableExistsAsync(fixture.ConnectionString, "doc_ab_timesheet")).Should().BeTrue();
        (await TableExistsAsync(fixture.ConnectionString, "doc_ab_customer_payment__applies")).Should().BeTrue();
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

    private static async Task<bool> TableExistsAsync(string cs, string tableName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*)::int
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name = @table;
            """,
            conn);

        cmd.Parameters.AddWithValue("table", tableName);
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }
}
