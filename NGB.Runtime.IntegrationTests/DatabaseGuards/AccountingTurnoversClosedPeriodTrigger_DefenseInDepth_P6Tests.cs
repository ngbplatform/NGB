using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

[Collection(PostgresCollection.Name)]
public sealed class AccountingTurnoversClosedPeriodTrigger_DefenseInDepth_P6Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = ReportingTestHelpers.Period; // 2026-01-01

    [Fact]
    public async Task Trigger_Forbids_InsertIntoTurnovers_ForClosedPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Period);

        Func<Task> act = () => InsertTurnoverRowAsync(
            Fixture.ConnectionString,
            Period,
            cashId,
            debitAmount: 10m,
            creditAmount: 0m);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");
    }

    [Fact]
    public async Task Trigger_Forbids_UpdateOnTurnovers_ForClosedPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await InsertTurnoverRowAsync(
            Fixture.ConnectionString,
            Period,
            cashId,
            debitAmount: 10m,
            creditAmount: 0m);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Period);

        Func<Task> act = () => UpdateTurnoverRowAsync(Fixture.ConnectionString, Period, cashId, delta: 1m);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");
    }

    [Fact]
    public async Task Trigger_Forbids_DeleteFromTurnovers_ForClosedPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await InsertTurnoverRowAsync(
            Fixture.ConnectionString,
            Period,
            cashId,
            debitAmount: 10m,
            creditAmount: 0m);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Period);

        Func<Task> act = () => DeleteTurnoverRowAsync(Fixture.ConnectionString, Period, cashId);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");
    }

    [Fact]
    public async Task Trigger_Allows_InsertIntoTurnovers_ForOpenPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await InsertTurnoverRowAsync(
            Fixture.ConnectionString,
            Period,
            cashId,
            debitAmount: 10m,
            creditAmount: 0m);

        (await CountTurnoverRowsAsync(Fixture.ConnectionString, Period, cashId)).Should().Be(1);
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
}
