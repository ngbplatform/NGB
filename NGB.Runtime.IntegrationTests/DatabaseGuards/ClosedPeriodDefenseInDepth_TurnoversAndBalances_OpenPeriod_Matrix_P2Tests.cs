using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P2: "Defense-in-depth" closed-period guards must not over-block legitimate operations
/// in OPEN periods. This complements P6 tests that assert INSERT/UPDATE/DELETE are forbidden
/// for CLOSED periods.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClosedPeriodDefenseInDepth_TurnoversAndBalances_OpenPeriod_Matrix_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = ReportingTestHelpers.Period; // 2026-01-01

    [Fact]
    public async Task Turnovers_OpenPeriod_Allows_Update_And_PersistsChange()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await InsertTurnoverRowAsync(Fixture.ConnectionString, Period, cashId, debitAmount: 10m, creditAmount: 0m);

        await UpdateTurnoverRowAsync(Fixture.ConnectionString, Period, cashId, delta: 1m);

        (await GetTurnoverDebitAsync(Fixture.ConnectionString, Period, cashId)).Should().Be(11m);
    }

    [Fact]
    public async Task Turnovers_OpenPeriod_Allows_Delete()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await InsertTurnoverRowAsync(Fixture.ConnectionString, Period, cashId, debitAmount: 10m, creditAmount: 0m);
        (await CountTurnoverRowsAsync(Fixture.ConnectionString, Period, cashId)).Should().Be(1);

        await DeleteTurnoverRowAsync(Fixture.ConnectionString, Period, cashId);

        (await CountTurnoverRowsAsync(Fixture.ConnectionString, Period, cashId)).Should().Be(0);
    }

    [Fact]
    public async Task Balances_OpenPeriod_Allows_Update_And_PersistsChange()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await InsertBalanceRowAsync(Fixture.ConnectionString, Period, cashId, openingBalance: 0m, closingBalance: 10m);

        await UpdateBalanceRowAsync(Fixture.ConnectionString, Period, cashId, delta: 1m);

        (await GetBalanceClosingAsync(Fixture.ConnectionString, Period, cashId)).Should().Be(11m);
    }

    [Fact]
    public async Task Balances_OpenPeriod_Allows_Delete()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await InsertBalanceRowAsync(Fixture.ConnectionString, Period, cashId, openingBalance: 0m, closingBalance: 10m);
        (await CountBalanceRowsAsync(Fixture.ConnectionString, Period, cashId)).Should().Be(1);

        await DeleteBalanceRowAsync(Fixture.ConnectionString, Period, cashId);

        (await CountBalanceRowsAsync(Fixture.ConnectionString, Period, cashId)).Should().Be(0);
    }

    private static async Task InsertTurnoverRowAsync(
        string cs,
        DateOnly period,
        Guid accountId,
        decimal debitAmount,
        decimal creditAmount,
        Guid dimensionSetId = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO accounting_turnovers
            (period, account_id, dimension_set_id, debit_amount, credit_amount)
            VALUES
            (@period, @account_id, @dimension_set_id, @debit_amount, @credit_amount);
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", dimensionSetId);
        cmd.Parameters.AddWithValue("debit_amount", debitAmount);
        cmd.Parameters.AddWithValue("credit_amount", creditAmount);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task UpdateTurnoverRowAsync(string cs, DateOnly period, Guid accountId, decimal delta, Guid dimensionSetId = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            UPDATE accounting_turnovers
            SET debit_amount = debit_amount + @delta
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

    private static async Task DeleteTurnoverRowAsync(string cs, DateOnly period, Guid accountId, Guid dimensionSetId = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            DELETE FROM accounting_turnovers
            WHERE period = @period
              AND account_id = @account_id
              AND dimension_set_id = @dimension_set_id;
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", dimensionSetId);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<int> CountTurnoverRowsAsync(string cs, DateOnly period, Guid accountId, Guid dimensionSetId = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM accounting_turnovers
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

    private static async Task<decimal> GetTurnoverDebitAsync(string cs, DateOnly period, Guid accountId, Guid dimensionSetId = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            SELECT debit_amount
            FROM accounting_turnovers
            WHERE period = @period
              AND account_id = @account_id
              AND dimension_set_id = @dimension_set_id;
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", dimensionSetId);

        var result = await cmd.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToDecimal(result);
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

    private static async Task<decimal> GetBalanceClosingAsync(string cs, DateOnly period, Guid accountId, Guid dimensionSetId = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            SELECT closing_balance
            FROM accounting_balances
            WHERE period = @period
              AND account_id = @account_id
              AND dimension_set_id = @dimension_set_id;
            """, conn);

        cmd.Parameters.AddWithValue("period", period.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("dimension_set_id", dimensionSetId);

        var result = await cmd.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToDecimal(result);
    }
}
