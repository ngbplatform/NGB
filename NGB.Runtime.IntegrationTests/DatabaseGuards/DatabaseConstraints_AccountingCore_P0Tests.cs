using Dapper;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: Database-level hard constraints must protect the platform even if a bug slips through
/// application-layer validators.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_AccountingCore_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime StartedAtUtc = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<(Guid CashId, Guid RevenueId)> InsertTwoAccountsAsync(NpgsqlConnection conn)
    {
        var cashId = Guid.CreateVersion7();
        var revenueId = Guid.CreateVersion7();

        // Minimal inserts that satisfy NOT NULL constraints and statement-section range.
        const string sql = """
                           INSERT INTO accounting_accounts(
                               account_id,
                               code,
                               name,
                               account_type,
                               statement_section,
                               negative_balance_policy,
                               is_active,
                               is_deleted
                           ) VALUES
                           (@CashId,   '50',   'Cash',   @CashType,   @Assets, @Allow, TRUE, FALSE),
                           (@RevenueId,'90.1', 'Revenue',@IncomeType, @Income, @Allow, TRUE, FALSE);
                           """;

        await conn.ExecuteAsync(sql, new
        {
            CashId = cashId,
            RevenueId = revenueId,
            CashType = (short)AccountType.Asset,
            IncomeType = (short)AccountType.Income,
            Assets = (short)StatementSection.Assets,
            Income = (short)StatementSection.Income,
            Allow = (short)NegativeBalancePolicy.Allow
        });

        return (cashId, revenueId);
    }

    [Fact]
    public async Task AccountingAccounts_CodeNormUnique_IsEnforcedByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var id1 = Guid.CreateVersion7();
        var id2 = Guid.CreateVersion7();

        // Same normalized code (case-insensitive) must violate ux_acc_accounts_code_norm.
        const string sql = """
                           INSERT INTO accounting_accounts(account_id, code, name, account_type, statement_section, negative_balance_policy)
                           VALUES
                           (@Id1, 'IT-CODE', 'A', @Type, @Section, @Policy),
                           (@Id2, 'it-code', 'B', @Type, @Section, @Policy);
                           """;

        var act = async () =>
        {
            await conn.ExecuteAsync(sql, new
            {
                Id1 = id1,
                Id2 = id2,
                Type = (short)AccountType.Asset,
                Section = (short)StatementSection.Assets,
                Policy = (short)NegativeBalancePolicy.Allow
            });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("ux_acc_accounts_code_norm");
    }

    [Fact]
    public async Task AccountingRegisterMain_ChecksAreEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var (cashId, revenueId) = await InsertTwoAccountsAsync(conn);

        // Amount must be > 0
        var actAmount = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_register_main(document_id, period, debit_account_id, credit_account_id, amount)
                VALUES (@DocId, @Period, @Debit, @Credit, 0);
                """,
                new
                {
                    DocId = Guid.CreateVersion7(),
                    Period = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                    Debit = revenueId,
                    Credit = cashId
                });
        };

        var exAmount = await actAmount.Should().ThrowAsync<PostgresException>();
        exAmount.Which.SqlState.Should().Be("23514");
        exAmount.Which.ConstraintName.Should().Be("ck_acc_reg_amount_positive");

        // Debit account must not equal credit account
        var actSame = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_register_main(document_id, period, debit_account_id, credit_account_id, amount)
                VALUES (@DocId, @Period, @Acc, @Acc, 10);
                """,
                new
                {
                    DocId = Guid.CreateVersion7(),
                    Period = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                    Acc = cashId
                });
        };

        var exSame = await actSame.Should().ThrowAsync<PostgresException>();
        exSame.Which.SqlState.Should().Be("23514");
        exSame.Which.ConstraintName.Should().Be("ck_acc_reg_debit_not_equal_credit");

        // FK must exist
        var actFk = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_register_main(document_id, period, debit_account_id, credit_account_id, amount)
                VALUES (@DocId, @Period, @Missing, @Credit, 10);
                """,
                new
                {
                    DocId = Guid.CreateVersion7(),
                    Period = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                    Missing = Guid.CreateVersion7(),
                    Credit = cashId
                });
        };

        var exFk = await actFk.Should().ThrowAsync<PostgresException>();
        exFk.Which.SqlState.Should().Be("23503");
        exFk.Which.ConstraintName.Should().Be("fk_acc_reg_debit_account");
    }

    [Fact]
    public async Task Turnovers_ChecksAreEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var (cashId, _) = await InsertTwoAccountsAsync(conn);
        var dim = Guid.Empty;

        // Period must be month start (day = 1)
        var actPeriod = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_turnovers(period, account_id, dimension_set_id, debit_amount, credit_amount)
                VALUES (@Period, @Acc, @Dim, 0, 0);
                """,
                new { Period = new DateOnly(2026, 1, 2), Acc = cashId, Dim = dim });
        };

        var exPeriod = await actPeriod.Should().ThrowAsync<PostgresException>();
        exPeriod.Which.SqlState.Should().Be("23514");
        exPeriod.Which.ConstraintName.Should().Be("chk_acc_turnovers_period_month_start");
    }

    [Fact]
    public async Task Balances_AndClosedPeriods_PeriodChecksAreEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var (cashId, _) = await InsertTwoAccountsAsync(conn);
        var dim = Guid.Empty;

        // Balances: period must be month start
        var actBalances = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance)
                VALUES (@Period, @Acc, @Dim, 0, 0);
                """,
                new { Period = new DateOnly(2026, 1, 15), Acc = cashId, Dim = dim });
        };

        var exBalances = await actBalances.Should().ThrowAsync<PostgresException>();
        exBalances.Which.SqlState.Should().Be("23514");
        exBalances.Which.ConstraintName.Should().Be("chk_acc_balances_period_month_start");

        // Closed periods: period must be month start
        var actClosed = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by)
                VALUES (@Period, @ClosedAt, 'it');
                """,
                new { Period = new DateOnly(2026, 1, 5), ClosedAt = StartedAtUtc });
        };

        var exClosed = await actClosed.Should().ThrowAsync<PostgresException>();
        exClosed.Which.SqlState.Should().Be("23514");
        exClosed.Which.ConstraintName.Should().Be("ck_closed_periods_month");
    }

    [Fact]
    public async Task PostingLog_ChecksAreEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var docId = Guid.CreateVersion7();

        // Operation must be in (1..4)
        var actOp = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_posting_state(document_id, operation, started_at_utc)
                VALUES (@DocId, 99, @Started);
                """,
                new { DocId = docId, Started = StartedAtUtc });
        };

        var exOp = await actOp.Should().ThrowAsync<PostgresException>();
        exOp.Which.SqlState.Should().Be("23514");
        exOp.Which.ConstraintName.Should().Be("ck_accounting_posting_state_operation");

        // completed_at_utc must be >= started_at_utc
        var actTime = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_posting_state(document_id, operation, started_at_utc, completed_at_utc)
                VALUES (@DocId2, 1, @Started, @Completed);
                """,
                new
                {
                    DocId2 = Guid.CreateVersion7(),
                    Started = StartedAtUtc,
                    Completed = StartedAtUtc.AddMinutes(-1)
                });
        };

        var exTime = await actTime.Should().ThrowAsync<PostgresException>();
        exTime.Which.SqlState.Should().Be("23514");
        exTime.Which.ConstraintName.Should().Be("ck_accounting_posting_state_completed_after_started");
    }
}
