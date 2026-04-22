using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// Regression guard: closed-period defense-in-depth must still block writes when DimensionSetId columns
/// are non-empty and satisfy FKs.
///
/// Why this matters:
/// - DimensionSetId is the long-term analytical key.
/// - With FK constraints enabled, it's easy for tests (or future code) to accidentally hit an FK error
///   instead of the intended closed-period error.
/// - This verifies the closed-period guard is the effective stop regardless of non-empty DimensionSetId.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClosedPeriodGuard_WithNonEmptyDimensionSets_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = ReportingTestHelpers.Period; // 2026-01-01

    [Fact]
    public async Task RegisterMain_Insert_WithNonEmptyDimensionSetIds_IsBlocked_ForClosedPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dimSetId = await SeedNonEmptyDimensionSetAsync(Fixture.ConnectionString);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Period);

        Func<Task> act = () => InsertRegisterRowAsync(
            Fixture.ConnectionString,
            documentId: Guid.CreateVersion7(),
            periodUtc: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            debitAccountId: cashId,
            creditAccountId: revenueId,
            debitDimensionSetId: dimSetId,
            creditDimensionSetId: dimSetId,
            amount: 10m);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");
    }

    [Fact]
    public async Task Turnovers_Insert_WithNonEmptyDimensionSetId_IsBlocked_ForClosedPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dimSetId = await SeedNonEmptyDimensionSetAsync(Fixture.ConnectionString);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Period);

        Func<Task> act = () => InsertTurnoverRowAsync(
            Fixture.ConnectionString,
            Period,
            cashId,
            dimSetId,
            debitAmount: 10m,
            creditAmount: 0m);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");
    }

    [Fact]
    public async Task Balances_Insert_WithNonEmptyDimensionSetId_IsBlocked_ForClosedPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dimSetId = await SeedNonEmptyDimensionSetAsync(Fixture.ConnectionString);

        await MarkPeriodClosedAsync(Fixture.ConnectionString, Period);

        Func<Task> act = () => InsertBalanceRowAsync(
            Fixture.ConnectionString,
            Period,
            cashId,
            dimSetId,
            openingBalance: 0m,
            closingBalance: 10m);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*Posting is forbidden. Period is closed:*2026-01-01*");
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

    private static async Task<Guid> SeedNonEmptyDimensionSetAsync(string cs)
    {
        // We want a non-empty DimensionSetId that is valid for FK checks.
        // The set does not need to be deterministic here; determinism is covered by DimensionSetService tests.
        var dimensionId = Guid.CreateVersion7();
        var dimensionSetId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        // Dimension definition.
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO platform_dimensions (dimension_id, code, name)
            VALUES (@dimension_id, @code, @name);
            """, conn))
        {
            cmd.Parameters.AddWithValue("dimension_id", dimensionId);
            cmd.Parameters.AddWithValue("code", "it_closed_period_dim");
            cmd.Parameters.AddWithValue("name", "IT Closed Period Dim");
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }

        // Dimension set header.
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO platform_dimension_sets (dimension_set_id)
            VALUES (@dimension_set_id)
            ON CONFLICT (dimension_set_id) DO NOTHING;
            """, conn))
        {
            cmd.Parameters.AddWithValue("dimension_set_id", dimensionSetId);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }

        // One item to ensure the set is truly non-empty from reporting perspective.
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO platform_dimension_set_items (dimension_set_id, dimension_id, value_id)
            VALUES (@dimension_set_id, @dimension_id, @value_id);
            """, conn))
        {
            cmd.Parameters.AddWithValue("dimension_set_id", dimensionSetId);
            cmd.Parameters.AddWithValue("dimension_id", dimensionId);
            cmd.Parameters.AddWithValue("value_id", valueId);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }

        return dimensionSetId;
    }

    private static async Task InsertRegisterRowAsync(
        string cs,
        Guid documentId,
        DateTime periodUtc,
        Guid debitAccountId,
        Guid creditAccountId,
        Guid debitDimensionSetId,
        Guid creditDimensionSetId,
        decimal amount)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO accounting_register_main
            (document_id, period,
             debit_account_id, credit_account_id,
             debit_dimension_set_id, credit_dimension_set_id,
             amount, is_storno)
            VALUES
            (@document_id, @period,
             @debit_account_id, @credit_account_id,
             @debit_dimension_set_id, @credit_dimension_set_id,
             @amount, FALSE);
            """, conn);

        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("period", periodUtc);
        cmd.Parameters.AddWithValue("debit_account_id", debitAccountId);
        cmd.Parameters.AddWithValue("credit_account_id", creditAccountId);
        cmd.Parameters.AddWithValue("debit_dimension_set_id", debitDimensionSetId);
        cmd.Parameters.AddWithValue("credit_dimension_set_id", creditDimensionSetId);
        cmd.Parameters.AddWithValue("amount", amount);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task InsertTurnoverRowAsync(
        string cs,
        DateOnly period,
        Guid accountId,
        Guid dimensionSetId,
        decimal debitAmount,
        decimal creditAmount)
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

    private static async Task InsertBalanceRowAsync(
        string cs,
        DateOnly period,
        Guid accountId,
        Guid dimensionSetId,
        decimal openingBalance,
        decimal closingBalance)
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
}
