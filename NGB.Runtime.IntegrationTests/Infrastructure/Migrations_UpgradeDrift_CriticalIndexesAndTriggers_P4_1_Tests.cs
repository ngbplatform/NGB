using Dapper;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_CriticalIndexesAndTriggers_P4_1_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrations_RecreatesDroppedCriticalIndexes()
    {
        // IMPORTANT:
        // Our migration runner is "CREATE IF NOT EXISTS" style.
        // This drift test verifies that dropping *indexes* is recoverable by re-applying migrations.

        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_acc_reg_period_month");
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_acc_turnovers_period_account");
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_acc_balances_period_account");

        // Sanity: dropped.
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_acc_reg_period_month")).Should().BeFalse();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_acc_turnovers_period_account")).Should().BeFalse();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_acc_balances_period_account")).Should().BeFalse();

        // Act
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: recreated.
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_acc_reg_period_month")).Should().BeTrue();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_acc_turnovers_period_account")).Should().BeTrue();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_acc_balances_period_account")).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyPlatformMigrations_RecreatesClosedPeriodGuardTriggers_IfDropped()
    {
        // Drop triggers. (Function is created via CREATE OR REPLACE; triggers use IF NOT EXISTS.)
        await ExecuteAsync(
            Fixture.ConnectionString,
            """
            DROP TRIGGER IF EXISTS trg_acc_reg_no_closed_period ON accounting_register_main;
            DROP TRIGGER IF EXISTS trg_acc_reg_no_closed_period_delete ON accounting_register_main;
            """);

        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_acc_reg_no_closed_period")).Should().BeFalse();
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_acc_reg_no_closed_period_delete")).Should().BeFalse();

        // Act
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_acc_reg_no_closed_period")).Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_acc_reg_no_closed_period_delete")).Should().BeTrue();
    }

    [Fact]
    public async Task AccountingRegister_PeriodMonth_IsGeneratedInUtc()
    {
        // The platform relies on period_month being computed in UTC, independent of session TimeZone.
        var expr = await GetGeneratedColumnExpressionAsync(
            Fixture.ConnectionString,
            table: "accounting_register_main",
            column: "period_month");

        expr.Should().NotBeNullOrWhiteSpace();
        expr!.Should().Contain("AT TIME ZONE 'UTC'");
        expr.Should().Contain("date_trunc('month'");
    }

    private static async Task ExecuteAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);
        await conn.ExecuteAsync(sql);
    }

    private static async Task DropIndexIfExistsAsync(string cs, string indexName)
    {
        await ExecuteAsync(cs, $"DROP INDEX IF EXISTS {indexName};");
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

    private static async Task<bool> TriggerExistsAsync(string cs, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM pg_trigger t
                WHERE t.tgname = @name
                  AND NOT t.tgisinternal
            ) THEN 1 ELSE 0 END;
            """,
            new { name = triggerName });

        return exists == 1;
    }

    private static async Task<string?> GetGeneratedColumnExpressionAsync(string cs, string table, string column)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        // NOTE:
        // Generated columns in PG are represented via pg_attrdef (expression) + pg_attribute.attgenerated.
        var expr = await conn.ExecuteScalarAsync<string?>(
            """
            SELECT pg_get_expr(ad.adbin, ad.adrelid)
            FROM pg_attrdef ad
            JOIN pg_attribute a
              ON a.attrelid = ad.adrelid
             AND a.attnum = ad.adnum
            WHERE ad.adrelid = to_regclass(@table)
              AND a.attname = @column;
            """,
            new { table, column });

        return expr;
    }
}
