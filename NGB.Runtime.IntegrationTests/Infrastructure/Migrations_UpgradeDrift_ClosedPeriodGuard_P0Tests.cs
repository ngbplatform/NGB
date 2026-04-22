using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_ClosedPeriodGuard_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrations_ReplacesBuggyNonUtcGuardFunction_AndUnblocksUtcMonthBoundary()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Close January 2026.
        await MarkPeriodClosedAsync(Fixture.ConnectionString, new DateOnly(2026, 1, 1));

        // This UTC timestamp is Feb 1, but in America/New_York it is still Jan 31 evening.
        var periodUtc = new DateTime(2026, 2, 1, 0, 30, 0, DateTimeKind.Utc);

        // Simulate an old buggy function body (local-time date_trunc) that would incorrectly map this to January.
        await ReplaceClosedPeriodGuardFunction_WithBuggyNonUtcVersionAsync(Fixture.ConnectionString);

        Func<Task> actBuggy = () => InsertRegisterRowAsync(
            Fixture.ConnectionString,
            documentId: Guid.CreateVersion7(),
            periodUtc: periodUtc,
            debitAccountId: cashId,
            creditAccountId: revenueId,
            amount: 10m,
            sessionTimeZone: "America/New_York");

        await actBuggy.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");

        // Re-apply platform migrations: AccountingClosedPeriodsGuardMigration must repair the function.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        Func<Task> actRepaired = () => InsertRegisterRowAsync(
            Fixture.ConnectionString,
            documentId: Guid.CreateVersion7(),
            periodUtc: periodUtc,
            debitAccountId: cashId,
            creditAccountId: revenueId,
            amount: 10m,
            sessionTimeZone: "America/New_York");

        await actRepaired.Should().NotThrowAsync();
    }

    private static async Task MarkPeriodClosedAsync(string cs, DateOnly period)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by)
            VALUES (@period, @closed_at, @closed_by);
            """,
            new
            {
                period = period.ToDateTime(TimeOnly.MinValue),
                closed_at = DateTime.UtcNow,
                closed_by = "test"
            });
    }

    private static async Task ReplaceClosedPeriodGuardFunction_WithBuggyNonUtcVersionAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        // Old buggy body: uses session time zone for date_trunc on TIMESTAMPTZ.
        await conn.ExecuteAsync(
            """
            CREATE OR REPLACE FUNCTION ngb_forbid_posting_into_closed_period()
            RETURNS trigger AS $$
            DECLARE
                p DATE;
            BEGIN
                p := date_trunc('month', NEW.period)::date;

                IF EXISTS (
                    SELECT 1
                    FROM accounting_closed_periods cp
                    WHERE cp.period = p
                ) THEN
                    RAISE EXCEPTION 'Posting is forbidden. Period is closed: %', p;
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            """);
    }

    private static async Task InsertRegisterRowAsync(
        string cs,
        Guid documentId,
        DateTime periodUtc,
        Guid debitAccountId,
        Guid creditAccountId,
        decimal amount,
        string sessionTimeZone)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await conn.ExecuteAsync("SELECT set_config('TimeZone', @tz, false);", new { tz = sessionTimeZone });

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_register_main
            (document_id, period, debit_account_id, credit_account_id, amount, is_storno)
            VALUES
            (@document_id, @period, @debit_account_id, @credit_account_id, @amount, FALSE);
            """,
            new
            {
                document_id = documentId,
                period = periodUtc,
                debit_account_id = debitAccountId,
                credit_account_id = creditAccountId,
                amount
            });
    }
}
