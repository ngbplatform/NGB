using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P3: A matrix test that verifies all time-zone-sensitive DB guards behave in UTC,
/// regardless of the PostgreSQL session TimeZone.
///
/// This is a regression net around:
/// - accounting_register_main.period_month generated column (UTC)
/// - closed-period guard triggers for INSERT/UPDATE/DELETE (UTC)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TimeZoneMatrix_AllDatabaseGuards_P3Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Jan = ReportingTestHelpers.Period;      // 2026-01-01
    private static readonly DateOnly Feb = ReportingTestHelpers.NextPeriod;  // 2026-02-01

    public static TheoryData<string> TimeZones => new()
    {
        "UTC",
        "America/New_York",
        "America/Los_Angeles",
        "Europe/Berlin",
        "Asia/Tokyo",
        "Pacific/Auckland",
    };

    [Theory]
    [MemberData(nameof(TimeZones))]
    public async Task ClosedPeriodGuard_Insert_IsBlocked_ForClosedUtcMonth_InAllTimeZones(string tz)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await MarkPeriodClosedAsync(Fixture.ConnectionString, Jan);

        var periodUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var docId = Guid.CreateVersion7();

        Func<Task> act = () => InsertRegisterRowReturningIdAsync(
            Fixture.ConnectionString,
            documentId: docId,
            periodUtc: periodUtc,
            debitAccountId: cashId,
            creditAccountId: revenueId,
            amount: 1m,
            sessionTimeZone: tz);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.MessageText.Should().Contain("Period is closed: 2026-01-01", "the guard must evaluate the month in UTC regardless of session TimeZone");
    }

    [Theory]
    [MemberData(nameof(TimeZones))]
    public async Task ClosedPeriodGuard_Insert_Allows_UtcBoundary_And_PeriodMonthIsUtc(string tz)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await MarkPeriodClosedAsync(Fixture.ConnectionString, Jan);

        // Feb 1st 00:30Z maps to Jan 31st in some time zones (e.g., America/New_York).
        // The DB must still treat this as February because month truncation is in UTC.
        var periodUtc = new DateTime(2026, 2, 1, 0, 30, 0, DateTimeKind.Utc);
        var docId = Guid.CreateVersion7();

        long id = 0;
        Func<Task> act = async () =>
        {
            id = await InsertRegisterRowReturningIdAsync(
                Fixture.ConnectionString,
                documentId: docId,
                periodUtc: periodUtc,
                debitAccountId: cashId,
                creditAccountId: revenueId,
                amount: 10m,
                sessionTimeZone: tz);
        };

        await act.Should().NotThrowAsync("period is February in UTC and January being closed must not block it");

        (await GetRegisterPeriodMonthAsync(Fixture.ConnectionString, id)).Should().Be(
            Feb.ToDateTime(TimeOnly.MinValue),
            "period_month must be computed in UTC");
    }

    [Theory]
    [MemberData(nameof(TimeZones))]
    public async Task ClosedPeriodGuard_Update_UsesUtcMonth_InAllTimeZones(string tz)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Insert an open-period row first (no closed period yet).
        var docId = Guid.CreateVersion7();
        var feb2 = new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc);
        var rowId = await InsertRegisterRowReturningIdAsync(
            Fixture.ConnectionString,
            documentId: docId,
            periodUtc: feb2,
            debitAccountId: cashId,
            creditAccountId: revenueId,
            amount: 10m,
            sessionTimeZone: null);

        // Now close January.
        await MarkPeriodClosedAsync(Fixture.ConnectionString, Jan);

        // Update period to Feb 1st boundary (must still be allowed in UTC).
        var febBoundary = new DateTime(2026, 2, 1, 0, 30, 0, DateTimeKind.Utc);
        Func<Task> updateToFebBoundary = () => UpdateRegisterPeriodAsync(Fixture.ConnectionString, rowId, febBoundary, tz);
        await updateToFebBoundary.Should().NotThrowAsync("the guard must evaluate month boundaries in UTC");

        (await GetRegisterPeriodMonthAsync(Fixture.ConnectionString, rowId)).Should().Be(Feb.ToDateTime(TimeOnly.MinValue));

        // Update amount only (period stays February) - must be allowed.
        Func<Task> updateAmount = () => UpdateRegisterAmountAsync(Fixture.ConnectionString, rowId, 11m, tz);
        await updateAmount.Should().NotThrowAsync("updating an open-period row must be allowed");

        // Update period into January (closed) - must be blocked.
        var jan31 = new DateTime(2026, 1, 31, 23, 30, 0, DateTimeKind.Utc);
        Func<Task> updateIntoJan = () => UpdateRegisterPeriodAsync(Fixture.ConnectionString, rowId, jan31, tz);
        var ex = await updateIntoJan.Should().ThrowAsync<PostgresException>();
        ex.Which.MessageText.Should().Contain("Period is closed: 2026-01-01");
    }

    [Theory]
    [MemberData(nameof(TimeZones))]
    public async Task ClosedPeriodGuard_Delete_UsesUtcMonth_InAllTimeZones(string tz)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Insert a January row BEFORE closing January.
        var janRowId = await InsertRegisterRowReturningIdAsync(
            Fixture.ConnectionString,
            documentId: Guid.CreateVersion7(),
            periodUtc: new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
            debitAccountId: cashId,
            creditAccountId: revenueId,
            amount: 5m,
            sessionTimeZone: null);

        // Insert a February row (should remain deletable).
        var febRowId = await InsertRegisterRowReturningIdAsync(
            Fixture.ConnectionString,
            documentId: Guid.CreateVersion7(),
            periodUtc: new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc),
            debitAccountId: cashId,
            creditAccountId: revenueId,
            amount: 5m,
            sessionTimeZone: null);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Jan);

        // Deleting the January row must be blocked.
        Func<Task> deleteJan = () => DeleteRegisterRowAsync(Fixture.ConnectionString, janRowId, tz);
        var ex = await deleteJan.Should().ThrowAsync<PostgresException>();
        ex.Which.MessageText.Should().Contain("Period is closed: 2026-01-01");

        // Deleting the February row must be allowed.
        Func<Task> deleteFeb = () => DeleteRegisterRowAsync(Fixture.ConnectionString, febRowId, tz);
        await deleteFeb.Should().NotThrowAsync();
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

    private static async Task<long> InsertRegisterRowReturningIdAsync(
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

        await SetSessionTimeZoneAsync(conn, sessionTimeZone);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO accounting_register_main
            (document_id, period, debit_account_id, credit_account_id, amount, is_storno)
            VALUES
            (@document_id, @period, @debit_account_id, @credit_account_id, @amount, FALSE)
            RETURNING entry_id;
            """, conn);

        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("period", periodUtc);
        cmd.Parameters.AddWithValue("debit_account_id", debitAccountId);
        cmd.Parameters.AddWithValue("credit_account_id", creditAccountId);
        cmd.Parameters.AddWithValue("amount", amount);

        var scalar = await cmd.ExecuteScalarAsync(CancellationToken.None);
        return (scalar is long l) ? l : Convert.ToInt64(scalar);
    }

    private static async Task UpdateRegisterPeriodAsync(string cs, long id, DateTime periodUtc, string? sessionTimeZone)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await SetSessionTimeZoneAsync(conn, sessionTimeZone);

        await using var cmd = new NpgsqlCommand("""
            UPDATE accounting_register_main
            SET period = @period
            WHERE entry_id = @id;
            """, conn);

        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("period", periodUtc);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task UpdateRegisterAmountAsync(string cs, long id, decimal amount, string? sessionTimeZone)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await SetSessionTimeZoneAsync(conn, sessionTimeZone);

        await using var cmd = new NpgsqlCommand("""
            UPDATE accounting_register_main
            SET amount = @amount
            WHERE entry_id = @id;
            """, conn);

        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("amount", amount);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task DeleteRegisterRowAsync(string cs, long id, string? sessionTimeZone)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await SetSessionTimeZoneAsync(conn, sessionTimeZone);

        await using var cmd = new NpgsqlCommand("""
            DELETE FROM accounting_register_main
            WHERE entry_id = @id;
            """, conn);

        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<DateTime> GetRegisterPeriodMonthAsync(string cs, long id)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            SELECT period_month
            FROM accounting_register_main
            WHERE entry_id = @id;
            """, conn);

        cmd.Parameters.AddWithValue("id", id);

        var scalar = await cmd.ExecuteScalarAsync(CancellationToken.None);

        return scalar switch
        {
            DateTime dt => dt.Date,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            string s => DateOnly.Parse(s).ToDateTime(TimeOnly.MinValue),
            null => throw new XunitException("period_month returned NULL"),
            _ => throw new XunitException($"Unexpected period_month type: {scalar.GetType().FullName}")
        };
    }

    private static async Task SetSessionTimeZoneAsync(NpgsqlConnection conn, string? sessionTimeZone)
    {
        if (string.IsNullOrWhiteSpace(sessionTimeZone))
            return;

        // PostgreSQL does not allow parameter placeholders in SET TIME ZONE.
        // Use set_config() to safely set the session TimeZone for this connection.
        await using var tz = new NpgsqlCommand("SELECT set_config('TimeZone', @tz, false);", conn);
        tz.Parameters.AddWithValue("tz", sessionTimeZone);
        await tz.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
