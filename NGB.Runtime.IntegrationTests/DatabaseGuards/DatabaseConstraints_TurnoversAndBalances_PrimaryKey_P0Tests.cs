using Dapper;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_TurnoversAndBalances_PrimaryKey_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Turnovers_PrimaryKeyPrevents_DuplicateRows_ForSameDimensionSet()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var accId = await InsertAccountAsync(conn, code: "50", name: "Cash");
        var period = new DateOnly(2026, 1, 1);
        var dimensionSetId = Guid.Empty;

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_turnovers(period, account_id, dimension_set_id, debit_amount, credit_amount)
            VALUES (@Period, @Acc, @Dim, 0, 0);
            """,
            new { Period = period, Acc = accId, Dim = dimensionSetId });

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_turnovers(period, account_id, dimension_set_id, debit_amount, credit_amount)
                VALUES (@Period, @Acc, @Dim, 0, 0);
                """,
                new { Period = period, Acc = accId, Dim = dimensionSetId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("accounting_turnovers_pkey");
    }

    [Fact]
    public async Task Balances_PrimaryKeyPrevents_DuplicateRows_ForSameDimensionSet()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var accId = await InsertAccountAsync(conn, code: "50", name: "Cash");
        var period = new DateOnly(2026, 1, 1);
        var dimensionSetId = Guid.Empty;

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance)
            VALUES (@Period, @Acc, @Dim, 0, 0);
            """,
            new { Period = period, Acc = accId, Dim = dimensionSetId });

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance)
                VALUES (@Period, @Acc, @Dim, 0, 0);
                """,
                new { Period = period, Acc = accId, Dim = dimensionSetId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("accounting_balances_pkey");
    }

    [Fact]
    public async Task Turnovers_PrimaryKey_AllowsDifferentAccounts_ForSameDimensionSet()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var accId = await InsertAccountAsync(conn, code: "50", name: "Cash");
        var accId2 = await InsertAccountAsync(conn, code: "51", name: "Bank");
        var period = new DateOnly(2026, 1, 1);
        var dimensionSetId = Guid.Empty;

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_turnovers(period, account_id, dimension_set_id, debit_amount, credit_amount)
            VALUES (@Period, @Acc, @Dim, 0, 0);
            """,
            new { Period = period, Acc = accId, Dim = dimensionSetId });

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_turnovers(period, account_id, dimension_set_id, debit_amount, credit_amount)
            VALUES (@Period, @Acc, @Dim, 0, 0);
            """,
            new { Period = period, Acc = accId2, Dim = dimensionSetId });

        var count = await conn.ExecuteScalarAsync<long>(
            """
            SELECT COUNT(*)
            FROM accounting_turnovers
            WHERE period = @Period AND dimension_set_id = @Dim;
            """,
            new { Period = period, Dim = dimensionSetId });

        count.Should().Be(2);
    }

    [Fact]
    public async Task Balances_PrimaryKey_AllowsDifferentAccounts_ForSameDimensionSet()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var accId = await InsertAccountAsync(conn, code: "50", name: "Cash");
        var accId2 = await InsertAccountAsync(conn, code: "51", name: "Bank");
        var period = new DateOnly(2026, 1, 1);
        var dimensionSetId = Guid.Empty;

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance)
            VALUES (@Period, @Acc, @Dim, 0, 0);
            """,
            new { Period = period, Acc = accId, Dim = dimensionSetId });

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_balances(period, account_id, dimension_set_id, opening_balance, closing_balance)
            VALUES (@Period, @Acc, @Dim, 0, 0);
            """,
            new { Period = period, Acc = accId2, Dim = dimensionSetId });

        var count = await conn.ExecuteScalarAsync<long>(
            """
            SELECT COUNT(*)
            FROM accounting_balances
            WHERE period = @Period AND dimension_set_id = @Dim;
            """,
            new { Period = period, Dim = dimensionSetId });

        count.Should().Be(2);
    }

    private static async Task<Guid> InsertAccountAsync(NpgsqlConnection conn, string code, string name)
    {
        var id = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_accounts(account_id, code, name, account_type, statement_section, negative_balance_policy)
            VALUES (@Id, @Code, @Name, @Type, @Section, @Policy);
            """,
            new
            {
                Id = id,
                Code = code,
                Name = name,
                Type = (short)AccountType.Asset,
                Section = (short)StatementSection.Assets,
                Policy = (short)NegativeBalancePolicy.Allow
            });

        return id;
    }
}
