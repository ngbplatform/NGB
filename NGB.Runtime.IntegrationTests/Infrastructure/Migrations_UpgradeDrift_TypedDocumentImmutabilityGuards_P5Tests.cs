using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P5: Drift recovery for the reusable typed-document immutability guard.
/// If triggers are dropped manually (or by a buggy migration), the idempotent bootstrap must restore them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_TypedDocumentImmutabilityGuards_P5Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrationsAsync_RecreatesTrgPostedImmutable_WhenDropped_FromTypedDocumentTables()
    {
        await Fixture.ResetDatabaseAsync();

        // Baseline: triggers exist.
        (await TriggerExistsAsync(Fixture.ConnectionString, "doc_general_journal_entry", "trg_posted_immutable"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "doc_general_journal_entry__lines", "trg_posted_immutable"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "doc_general_journal_entry__allocations", "trg_posted_immutable"))
            .Should().BeTrue();

        // Simulate drift.
        await DropTriggerAsync(Fixture.ConnectionString, "doc_general_journal_entry", "trg_posted_immutable");
        await DropTriggerAsync(Fixture.ConnectionString, "doc_general_journal_entry__lines", "trg_posted_immutable");
        await DropTriggerAsync(Fixture.ConnectionString, "doc_general_journal_entry__allocations", "trg_posted_immutable");

        (await TriggerExistsAsync(Fixture.ConnectionString, "doc_general_journal_entry", "trg_posted_immutable"))
            .Should().BeFalse();

        // Act: re-apply migrations.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: installer is re-runnable and must restore the triggers.
        (await TriggerExistsAsync(Fixture.ConnectionString, "doc_general_journal_entry", "trg_posted_immutable"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "doc_general_journal_entry__lines", "trg_posted_immutable"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "doc_general_journal_entry__allocations", "trg_posted_immutable"))
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
