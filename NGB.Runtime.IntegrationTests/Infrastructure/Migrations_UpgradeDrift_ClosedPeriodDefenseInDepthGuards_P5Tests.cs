using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P5: Drift recovery for defense-in-depth closed-period guards on turnovers and balances.
/// This is intentionally redundant with the operational write-path and blocks SQL bypass.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_ClosedPeriodDefenseInDepthGuards_P5Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrationsAsync_RecreatesClosedPeriodGuards_WhenDropped_FromTurnoversAndBalances()
    {
        await Fixture.ResetDatabaseAsync();

        // Baseline: triggers exist.
        (await TriggerExistsAsync(Fixture.ConnectionString, "accounting_turnovers", "trg_acc_turnovers_no_closed_period"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "accounting_turnovers", "trg_acc_turnovers_no_closed_period_delete"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "accounting_balances", "trg_acc_balances_no_closed_period"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "accounting_balances", "trg_acc_balances_no_closed_period_delete"))
            .Should().BeTrue();

        // Simulate drift.
        await DropTriggerAsync(Fixture.ConnectionString, "accounting_turnovers", "trg_acc_turnovers_no_closed_period");
        await DropTriggerAsync(Fixture.ConnectionString, "accounting_turnovers", "trg_acc_turnovers_no_closed_period_delete");
        await DropTriggerAsync(Fixture.ConnectionString, "accounting_balances", "trg_acc_balances_no_closed_period");
        await DropTriggerAsync(Fixture.ConnectionString, "accounting_balances", "trg_acc_balances_no_closed_period_delete");

        (await TriggerExistsAsync(Fixture.ConnectionString, "accounting_turnovers", "trg_acc_turnovers_no_closed_period"))
            .Should().BeFalse();

        // Act: re-apply migrations.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: triggers are restored.
        (await TriggerExistsAsync(Fixture.ConnectionString, "accounting_turnovers", "trg_acc_turnovers_no_closed_period"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "accounting_turnovers", "trg_acc_turnovers_no_closed_period_delete"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "accounting_balances", "trg_acc_balances_no_closed_period"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "accounting_balances", "trg_acc_balances_no_closed_period_delete"))
            .Should().BeTrue();
    }

    private static async Task DropTriggerAsync(string cs, string tableName, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {triggerName} ON public.{tableName};", conn);
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
            """, conn);

        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("trigger", triggerName);

        var count = (int)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }
}
