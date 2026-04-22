using Dapper;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: Database-level hard constraints for accounting_account_dimension_rules.
/// Ensures PK/FK/check/cascade invariants are enforced even if application validators are bypassed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_AccountingAccountDimensionRules_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static async Task<Guid> InsertAccountAsync(NpgsqlConnection conn)
    {
        var id = Guid.CreateVersion7();

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
                           )
                           VALUES (
                               @Id,
                               @Code,
                               @Name,
                               @Type,
                               @Section,
                               @Policy,
                               TRUE,
                               FALSE
                           );
                           """;

        await conn.ExecuteAsync(sql, new
        {
            Id = id,
            Code = $"IT-{id:N}".Substring(0, 10),
            Name = "IT account",
            Type = (short)AccountType.Asset,
            Section = (short)StatementSection.Assets,
            Policy = (short)NegativeBalancePolicy.Allow
        });

        return id;
    }

    private static async Task<Guid> InsertDimensionAsync(NpgsqlConnection conn)
    {
        var id = Guid.CreateVersion7();

        const string sql = """
                           INSERT INTO platform_dimensions(
                               dimension_id,
                               code,
                               name,
                               is_active,
                               is_deleted
                           )
                           VALUES (
                               @Id,
                               @Code,
                               @Name,
                               TRUE,
                               FALSE
                           );
                           """;

        await conn.ExecuteAsync(sql, new
        {
            Id = id,
            Code = $"DIM-{id:N}".Substring(0, 12),
            Name = "IT dimension"
        });

        return id;
    }

    [Fact]
    public async Task Ordinal_MustBePositive_CheckIsEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var accountId = await InsertAccountAsync(conn);
        var dimId = await InsertDimensionAsync(conn);

        Func<Task> act = () => conn.ExecuteAsync(
            """
            INSERT INTO accounting_account_dimension_rules(account_id, dimension_id, ordinal, is_required)
            VALUES (@AccountId, @DimensionId, @Ordinal, FALSE);
            """,
            new { AccountId = accountId, DimensionId = dimId, Ordinal = 0 });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        ex.Which.ConstraintName.Should().Be("ck_acc_dim_rules_ordinal_positive");
    }

    [Fact]
    public async Task PrimaryKey_EnforcesOneRowPerAccountAndDimension()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var accountId = await InsertAccountAsync(conn);
        var dimId = await InsertDimensionAsync(conn);

        const string insertSql = """
                                 INSERT INTO accounting_account_dimension_rules(account_id, dimension_id, ordinal, is_required)
                                 VALUES (@AccountId, @DimensionId, @Ordinal, FALSE);
                                 """;

        await conn.ExecuteAsync(insertSql, new { AccountId = accountId, DimensionId = dimId, Ordinal = 10 });

        Func<Task> act = () => conn.ExecuteAsync(insertSql, new { AccountId = accountId, DimensionId = dimId, Ordinal = 20 });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        ex.Which.ConstraintName.Should().Be("pk_acc_dim_rules");
    }

    [Fact]
    public async Task ForeignKey_Account_IsEnforced()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var missingAccountId = Guid.CreateVersion7();
        var dimId = await InsertDimensionAsync(conn);

        Func<Task> act = () => conn.ExecuteAsync(
            """
            INSERT INTO accounting_account_dimension_rules(account_id, dimension_id, ordinal, is_required)
            VALUES (@AccountId, @DimensionId, 10, FALSE);
            """,
            new { AccountId = missingAccountId, DimensionId = dimId });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        ex.Which.ConstraintName.Should().Be("fk_acc_dim_rules_account");
    }

    [Fact]
    public async Task ForeignKey_Dimension_IsEnforced()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var accountId = await InsertAccountAsync(conn);
        var missingDimId = Guid.CreateVersion7();

        Func<Task> act = () => conn.ExecuteAsync(
            """
            INSERT INTO accounting_account_dimension_rules(account_id, dimension_id, ordinal, is_required)
            VALUES (@AccountId, @DimensionId, 10, FALSE);
            """,
            new { AccountId = accountId, DimensionId = missingDimId });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        ex.Which.ConstraintName.Should().Be("fk_acc_dim_rules_dimension");
    }

    [Fact]
    public async Task DeletingAccount_CascadesDimensionRules()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var accountId = await InsertAccountAsync(conn);
        var dimId = await InsertDimensionAsync(conn);

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_account_dimension_rules(account_id, dimension_id, ordinal, is_required)
            VALUES (@AccountId, @DimensionId, 10, TRUE);
            """,
            new { AccountId = accountId, DimensionId = dimId });

        await conn.ExecuteAsync(
            "DELETE FROM accounting_accounts WHERE account_id = @Id;",
            new { Id = accountId });

        var cnt = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_account_dimension_rules WHERE account_id = @Id;",
            new { Id = accountId });

        cnt.Should().Be(0, "dimension rules must be removed when account is deleted (FK ... ON DELETE CASCADE)");
    }
}
