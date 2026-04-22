using Dapper;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using System.Globalization;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

[Collection(PostgresCollection.Name)]
public sealed class AccountingRegister_PeriodMonth_UtcBoundary_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PeriodMonth_IsComputedInUtc_EvenWhenSessionTimeZoneWouldMapToPreviousMonth()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var cashId = Guid.CreateVersion7();
        var revenueId = Guid.CreateVersion7();

        // Minimal inserts that satisfy NOT NULL constraints.
        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_accounts(account_id, code, name, account_type, statement_section, negative_balance_policy)
            VALUES
                (@CashId, '50', 'Cash', @Asset, @Assets, @Allow),
                (@RevenueId, '90.1', 'Revenue', @IncomeType, @Income, @Allow);
            """,
            new
            {
                CashId = cashId,
                RevenueId = revenueId,
                Asset = (short)AccountType.Asset,
                IncomeType = (short)AccountType.Income,
                Assets = (short)StatementSection.Assets,
                Income = (short)StatementSection.Income,
                Allow = (short)NegativeBalancePolicy.Allow
            });

        // This UTC timestamp is Feb 1, but in America/New_York it is still Jan 31 evening.
        var periodUtc = new DateTime(2026, 2, 1, 0, 30, 0, DateTimeKind.Utc);
        var docId = Guid.CreateVersion7();

        // Ensure session time zone does not influence period_month calculation.
        await conn.ExecuteAsync("SELECT set_config('TimeZone', @tz, false);", new { tz = "America/New_York" });

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_register_main(document_id, period, debit_account_id, credit_account_id, amount)
            VALUES (@DocId, @Period, @Debit, @Credit, 10);
            """,
            new
            {
                DocId = docId,
                Period = periodUtc,
                Debit = cashId,
                Credit = revenueId
            });

        // Dapper.AOT: avoid `object` / dynamic materialization.
        var periodMonthText = await conn.ExecuteScalarAsync<string?>(
            """
            SELECT (period_month::date)::text
            FROM accounting_register_main
            WHERE document_id = @DocId;
            """,
            new { DocId = docId });

        var asDateOnly = DateOnly.ParseExact(
            periodMonthText ?? throw new InvalidOperationException("accounting_register_main.period_month was null."),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);
        asDateOnly.Should().Be(new DateOnly(2026, 2, 1));
    }
}
