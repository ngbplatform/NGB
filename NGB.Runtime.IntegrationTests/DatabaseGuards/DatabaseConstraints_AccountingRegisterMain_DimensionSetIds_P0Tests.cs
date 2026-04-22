using Dapper;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: accounting_register_main must enforce DimensionSetId FK invariants at the DB level.
/// This protects the platform even if application-layer validation is bypassed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_AccountingRegisterMain_DimensionSetIds_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

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
    public async Task RegisterMain_DefaultDimensionSetIds_AreGuidEmpty_AndFkIsValid()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var (cashId, revenueId) = await InsertTwoAccountsAsync(conn);
        var docId = Guid.CreateVersion7();

        // Omit dimension_set_id columns -> defaults to Guid.Empty.
        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_register_main(document_id, period, debit_account_id, credit_account_id, amount)
            VALUES (@DocId, @Period, @Debit, @Credit, 10);
            """,
            new { DocId = docId, Period = PeriodUtc, Debit = revenueId, Credit = cashId });

        var row = await conn.QuerySingleAsync<(Guid DebitDim, Guid CreditDim)>(
            """
            SELECT debit_dimension_set_id AS DebitDim,
                   credit_dimension_set_id AS CreditDim
            FROM accounting_register_main
            WHERE document_id = @DocId
            ORDER BY entry_id DESC
            LIMIT 1;
            """,
            new { DocId = docId });

        row.DebitDim.Should().Be(Guid.Empty);
        row.CreditDim.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task RegisterMain_FkAccRegDebitDimensionSet_IsEnforced()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var (cashId, revenueId) = await InsertTwoAccountsAsync(conn);

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_register_main(
                    document_id, period,
                    debit_account_id, credit_account_id,
                    debit_dimension_set_id, credit_dimension_set_id,
                    amount
                )
                VALUES (@DocId, @Period, @Debit, @Credit, @BadDim, @Empty, 10);
                """,
                new
                {
                    DocId = Guid.CreateVersion7(),
                    Period = PeriodUtc,
                    Debit = revenueId,
                    Credit = cashId,
                    BadDim = Guid.CreateVersion7(),
                    Empty = Guid.Empty
                });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23503");
        ex.Which.ConstraintName.Should().Be("fk_acc_reg_debit_dimension_set");
    }

    [Fact]
    public async Task RegisterMain_FkAccRegCreditDimensionSet_IsEnforced()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var (cashId, revenueId) = await InsertTwoAccountsAsync(conn);

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_register_main(
                    document_id, period,
                    debit_account_id, credit_account_id,
                    debit_dimension_set_id, credit_dimension_set_id,
                    amount
                )
                VALUES (@DocId, @Period, @Debit, @Credit, @Empty, @BadDim, 10);
                """,
                new
                {
                    DocId = Guid.CreateVersion7(),
                    Period = PeriodUtc,
                    Debit = revenueId,
                    Credit = cashId,
                    BadDim = Guid.CreateVersion7(),
                    Empty = Guid.Empty
                });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23503");
        ex.Which.ConstraintName.Should().Be("fk_acc_reg_credit_dimension_set");
    }

    [Fact]
    public async Task RegisterMain_CanReferenceExistingNonEmptyDimensionSetId()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var (cashId, revenueId) = await InsertTwoAccountsAsync(conn);

        var dimSetId = Guid.CreateVersion7();

        // Minimal header is enough to satisfy FK.
        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_sets(dimension_set_id)
            VALUES (@Id);
            """,
            new { Id = dimSetId });

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_register_main(
                document_id, period,
                debit_account_id, credit_account_id,
                debit_dimension_set_id, credit_dimension_set_id,
                amount
            )
            VALUES (@DocId, @Period, @Debit, @Credit, @Dim, @Empty, 10);
            """,
            new
            {
                DocId = Guid.CreateVersion7(),
                Period = PeriodUtc,
                Debit = revenueId,
                Credit = cashId,
                Dim = dimSetId,
                Empty = Guid.Empty
            });

        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM accounting_register_main
            WHERE debit_dimension_set_id = @Dim;
            """,
            new { Dim = dimSetId });

        exists.Should().Be(1);
    }

    [Fact]
    public async Task RegisterMain_DimensionSetIdColumns_AreNotNull()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var (cashId, revenueId) = await InsertTwoAccountsAsync(conn);

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_register_main(
                    document_id, period,
                    debit_account_id, credit_account_id,
                    debit_dimension_set_id,
                    amount
                )
                VALUES (@DocId, @Period, @Debit, @Credit, NULL, 10);
                """,
                new
                {
                    DocId = Guid.CreateVersion7(),
                    Period = PeriodUtc,
                    Debit = revenueId,
                    Credit = cashId
                });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23502");
        ex.Which.ColumnName.Should().Be("debit_dimension_set_id");
    }
}
