using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

[Collection(PostgresCollection.Name)]
public sealed class AccountingBalancesClosedPeriodTrigger_DefenseInDepth_P6Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = ReportingTestHelpers.Period; // 2026-01-01

    [Fact]
    public async Task Trigger_Forbids_InsertIntoBalances_ForClosedPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Period);

        Func<Task> act = () => InsertBalanceRowAsync(
            Fixture.ConnectionString,
            Period,
            cashId,
            openingBalance: 0m,
            closingBalance: 10m);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");
    }

    [Fact]
    public async Task Trigger_Forbids_UpdateOnBalances_ForClosedPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await InsertBalanceRowAsync(
            Fixture.ConnectionString,
            Period,
            cashId,
            openingBalance: 0m,
            closingBalance: 10m);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Period);

        Func<Task> act = () => UpdateBalanceRowAsync(Fixture.ConnectionString, Period, cashId, delta: 1m);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");
    }

    [Fact]
    public async Task Trigger_Forbids_DeleteFromBalances_ForClosedPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await InsertBalanceRowAsync(
            Fixture.ConnectionString,
            Period,
            cashId,
            openingBalance: 0m,
            closingBalance: 10m);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Period);

        Func<Task> act = () => DeleteBalanceRowAsync(Fixture.ConnectionString, Period, cashId);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");
    }

    [Fact]
    public async Task Trigger_Allows_InsertIntoBalances_ForOpenPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await InsertBalanceRowAsync(
            Fixture.ConnectionString,
            Period,
            cashId,
            openingBalance: 0m,
            closingBalance: 10m);

        (await CountBalanceRowsAsync(Fixture.ConnectionString, Period, cashId)).Should().Be(1);
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

    private static async Task InsertBalanceRowAsync(
        string cs,
        DateOnly period,
        Guid accountId,
        decimal openingBalance,
        decimal closingBalance,
        Guid dimensionSetId = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO accounting_balances
            (period, account_id, dimension_set_id, opening_balance, closing_balance)
            VALUES
            (@period, @account_id, @dimension_set_id, @opening_balance, @closing_balance);
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", dimensionSetId);
        cmd.Parameters.AddWithValue("opening_balance", openingBalance);
        cmd.Parameters.AddWithValue("closing_balance", closingBalance);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task UpdateBalanceRowAsync(string cs, DateOnly period, Guid accountId, decimal delta, Guid dimensionSetId = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            UPDATE accounting_balances
            SET closing_balance = closing_balance + @delta
            WHERE period = @period
              AND account_id = @account_id
              AND dimension_set_id = @dimension_set_id;
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", dimensionSetId);
        cmd.Parameters.AddWithValue("delta", delta);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task DeleteBalanceRowAsync(string cs, DateOnly period, Guid accountId, Guid dimensionSetId = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            DELETE FROM accounting_balances
            WHERE period = @period
              AND account_id = @account_id
              AND dimension_set_id = @dimension_set_id;
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", dimensionSetId);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<int> CountBalanceRowsAsync(string cs, DateOnly period, Guid accountId, Guid dimensionSetId = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM accounting_balances
            WHERE period = @period
              AND account_id = @account_id
              AND dimension_set_id = @dimension_set_id;
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", dimensionSetId);

        var result = await cmd.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToInt32(result);
    }
}
