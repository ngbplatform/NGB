using FluentAssertions;
using NGB.AgencyBilling.PostgreSql.Bootstrap;
using NGB.AgencyBilling.PostgreSql.Migrations;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;
using Npgsql;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;

[Collection(AgencyBillingSchemaPostgresCollection.Name)]
public sealed class AgencyBillingSchemaMigrator_NoRepair_P0Tests(AgencyBillingSchemaPostgresFixture fixture)
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

    [Fact]
    public async Task Repair_WithoutExplicitOptions_CompletesAgainstMigratedSchema()
    {
        await fixture.ResetDatabaseAsync();

        var act = () => AgencyBillingDatabaseBootstrapper.RepairModuleAsync(fixture.ConnectionString);

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

        var act = () => AgencyBillingDatabaseBootstrapper.RepairModuleAsync(
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
