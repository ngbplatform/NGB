using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: Closed-period guard trigger must evaluate month boundaries in UTC,
/// not in the current PostgreSQL session time zone.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountingRegisterClosedPeriodTrigger_TimeZoneBoundary_UsesUtc_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Jan = ReportingTestHelpers.Period; // 2026-01-01

    [Fact]
    public async Task Trigger_Allows_InsertAtUtcMonthBoundary_WhenSessionTimeZoneWouldMapToPreviousMonth()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await MarkPeriodClosedAsync(Fixture.ConnectionString, Jan);

        // Feb 1st 00:30Z is still Jan 31st 19:30 in America/New_York.
        // If the trigger truncates by the session time zone, it will incorrectly treat this as January.
        var periodUtc = new DateTime(2026, 2, 1, 0, 30, 0, DateTimeKind.Utc);
        var docId = Guid.CreateVersion7();

        Func<Task> act = () => InsertRegisterRowAsync(
            Fixture.ConnectionString,
            documentId: docId,
            periodUtc: periodUtc,
            debitAccountId: cashId,
            creditAccountId: revenueId,
            amount: 10m,
            sessionTimeZone: "America/New_York");

        await act.Should().NotThrowAsync("period is February in UTC and January being closed must not block it");

        (await CountRegisterRowsAsync(Fixture.ConnectionString, docId)).Should().Be(1);
    }

    private static async Task MarkPeriodClosedAsync(string cs, DateOnly period)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by)
            VALUES (@period, @closed_at, @closed_by);
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("closed_at", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("closed_by", "test");

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task InsertRegisterRowAsync(
        string cs,
        Guid documentId,
        DateTime periodUtc,
        Guid debitAccountId,
        Guid creditAccountId,
        decimal amount,
        string? sessionTimeZone)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        if (!string.IsNullOrWhiteSpace(sessionTimeZone))
        {
            // PostgreSQL does not allow parameter placeholders in SET TIME ZONE.
            // Use set_config() to safely set the session TimeZone for this connection.
            await using var tz = new NpgsqlCommand("SELECT set_config('TimeZone', @tz, false);", conn);
            tz.Parameters.AddWithValue("tz", sessionTimeZone);
            await tz.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO accounting_register_main
            (document_id, period, debit_account_id, credit_account_id,
             amount, is_storno)
            VALUES
            (@document_id, @period, @debit_account_id, @credit_account_id,
             @amount, FALSE);
            """, conn);

        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("period", periodUtc);
        cmd.Parameters.AddWithValue("debit_account_id", debitAccountId);
        cmd.Parameters.AddWithValue("credit_account_id", creditAccountId);
        cmd.Parameters.AddWithValue("amount", amount);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<long> CountRegisterRowsAsync(string cs, Guid documentId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM accounting_register_main
            WHERE document_id = @document_id;
            """, conn);

        cmd.Parameters.AddWithValue("document_id", documentId);

        var scalar = await cmd.ExecuteScalarAsync(CancellationToken.None);
        return (scalar is long l) ? l : Convert.ToInt64(scalar);
    }
}
