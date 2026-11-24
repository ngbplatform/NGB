using Dapper;
using Npgsql;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class DatedWorkflowTests
{
    private static async Task<NpgsqlConnection> OpenAsync(TestDatabase database)
    {
        var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "work-calendar.sql")));
        await connection.ExecuteAsync("""
            INSERT INTO work_example.resource (id, stable_code, contact, business_zone, category)
            VALUES (1, 'M-01', 'operator@example.test', 'America/New_York', 'Standard')
            """);
        return connection;
    }

    [Fact]
    public async Task Credits_and_entitlements_must_exist_on_each_dependent_date_even_after_a_replacement()
    {
        await using var database = await (await TestInfrastructure.GetAsync()).CreateDatabaseAsync();
        await using var connection = await OpenAsync(database);

        Task<long> Post(string day, int credit, int right) => connection.ExecuteScalarAsync<long>(
            "SELECT work_example.change_position(1, CAST(@day AS date), @credit, @right)", new { day, credit, right });
        Task<long> Withdraw(long id) => connection.ExecuteScalarAsync<long>(
            "SELECT work_example.change_position(1, NULL, 0, 0, @id)", new { id });
        Task<string?> State() => connection.ExecuteScalarAsync<string>(
            "SELECT jsonb_agg(to_jsonb(m) ORDER BY id)::text FROM work_example.movement m");

        var original = await Post("2026-04-02", 20, 0);
        await Post("2026-04-04", 0, 1);
        var initial = await State();
        await RefusedAsync(() => Post("2026-04-01", -20, 0));
        await RefusedAsync(() => Post("2026-04-03", -20, -1));
        Assert.Equal(initial, await State());

        await Post("2026-04-04", -20, -1);
        await Post("2026-04-05", 20, 0);
        var committed = await State();
        await RefusedAsync(() => Withdraw(original));
        Assert.Equal(committed, await State());

        const string position = "SELECT work_example.credit_balance(1, CAST(@day AS date))";
        foreach (var zone in new[] { "Pacific/Kiritimati", "America/New_York" })
        {
            Assert.Equal(zone, await connection.ExecuteScalarAsync<string>(
                "SELECT set_config('TimeZone', @zone, false)", new { zone }));
            foreach (var (day, expected) in new[] { ("2026-04-01", 0), ("2026-04-02", 20), ("2026-04-03", 20), ("2026-04-04", 0) })
                Assert.Equal(expected, await connection.ExecuteScalarAsync<int>(position, new { day }));
        }

        await Post("2026-04-02", 20, 0);
        Assert.Equal(original, await Withdraw(original));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(position, new { day = "2026-04-04" }));
    }

    [Fact]
    public async Task Open_and_completed_work_preserve_captured_calendar_category_and_amount()
    {
        await using var database = await (await TestInfrastructure.GetAsync()).CreateDatabaseAsync();
        await using var connection = await OpenAsync(database);
        const string firstStart = "2026-04-01T03:30:00Z";
        const string firstEnd = "2026-04-01T04:30:00Z";
        Task<long> Start(string instant) => connection.ExecuteScalarAsync<long>(
            "SELECT work_example.begin_assignment(1, CAST(@instant AS timestamptz), 10)", new { instant });
        Task<int> Finish(long id, string instant) => connection.ExecuteAsync(
            "UPDATE work_example.assignment SET finished = CAST(@instant AS timestamptz) WHERE id = @id AND finished IS NULL",
            new { id, instant });

        var first = await Start(firstStart);
        await RefusedAsync(() => Finish(first, firstStart));
        Assert.False(await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM work_example.completed_charges WHERE id = @first)", new { first }));
        await connection.ExecuteAsync("UPDATE work_example.resource SET business_zone = 'UTC' WHERE id = 1");
        Assert.Equal(1, await Finish(first, firstEnd));
        await connection.ExecuteAsync("UPDATE work_example.resource SET category = 'Special' WHERE id = 1");
        await RefusedAsync(() => Start("2026-04-01T03:45:00Z"));
        var second = await Start("2026-04-02T03:30:00Z");
        Assert.Equal(1, await Finish(second, "2026-04-02T04:30:00Z"));
        await RefusedAsync(() => connection.ExecuteAsync("""
            UPDATE work_example.assignment SET finished = '2026-04-02T03:45:00Z' WHERE id = @first
            """, new { first }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("""
            SELECT count(*) FROM work_example.assignment WHERE id = @first AND finished = CAST(@firstEnd AS timestamptz)
            """, new { first, firstEnd }));

        var groups = (await connection.QueryAsync<(string Category, decimal Amount)>("""
            SELECT captured_category, sum(amount) FROM work_example.completed_charges
            GROUP BY captured_category ORDER BY captured_category
            """)).ToArray();
        Assert.Equal(new[] { ("Special", 10m), ("Standard", 20m) }, groups);
    }

    [Fact]
    public async Task Retired_identity_cannot_be_reactivated_or_reused_and_required_contact_survives_failed_edits()
    {
        await using var database = await (await TestInfrastructure.GetAsync()).CreateDatabaseAsync();
        await using var connection = await OpenAsync(database);
        const string resource = "UPDATE work_example.resource SET ";
        Assert.Equal(1, await connection.ExecuteAsync(resource + "contact = 'reviewer@example.test' WHERE id = 1"));
        foreach (var blank in new[] { "  ", "\t\n" })
        {
            await RefusedAsync(() => connection.ExecuteAsync(resource + "contact = @blank WHERE id = 1", new { blank }));
        }
        Assert.Equal(1, await connection.ExecuteAsync(resource + "retired = true WHERE id = 1"));
        await RefusedAsync(() => connection.ExecuteAsync(resource + "stable_code = 'M-02' WHERE id = 1"));
        await RefusedAsync(() => connection.ExecuteAsync(resource + "retired = false WHERE id = 1"));
        await RefusedAsync(() => connection.ExecuteAsync("DELETE FROM work_example.resource WHERE id = 1"));
        await RefusedAsync(() => connection.ExecuteAsync("SELECT work_example.begin_assignment(1, now(), 10)"));
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync("""
            INSERT INTO work_example.resource (id, stable_code, contact, business_zone, category)
            VALUES (2, 'M-01', 'next@example.test', 'UTC', 'New')
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        Assert.Equal(("M-01", "reviewer@example.test", true), await connection.QuerySingleAsync<(string, string, bool)>(
            "SELECT stable_code, contact, retired FROM work_example.resource WHERE id = 1"));
    }

    private static async Task RefusedAsync(Func<Task> operation)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(operation);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }
}
