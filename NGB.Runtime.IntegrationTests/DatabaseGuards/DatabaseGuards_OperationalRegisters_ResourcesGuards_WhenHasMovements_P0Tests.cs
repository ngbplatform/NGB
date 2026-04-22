using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: DB-level immutability guard for operational_register_resources after a register has movements.
///
/// Even if a caller bypasses runtime services and attempts to mutate resource identifiers,
/// the database must protect per-register physical schema and historical movements integrity.
///
/// Notes:
/// - After has_movements = TRUE, the DB trigger forbids:
///   - DELETE
///   - changing code/code_norm/column_code/register_id
/// - But it still allows forward-only evolution:
///   - updating user-facing fields (name/ordinal)
///   - inserting new optional resources (new columns in future movements)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseGuards_OperationalRegisters_ResourcesGuards_WhenHasMovements_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Resources_AreImmutableAfterHasMovements_ButNameAndOrdinalAreEditable_AndInsertsAreAllowed()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var regId = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            "INSERT INTO operational_registers(register_id, code, name) VALUES (@Id, 'RR', 'Rent Roll');",
            new { Id = regId });

        await conn.ExecuteAsync(
            """
            INSERT INTO operational_register_resources(register_id, code, code_norm, column_code, name, ordinal)
            VALUES (@RegId, 'AMOUNT', 'amount', 'amount', 'Amount', 10);
            """,
            new { RegId = regId });

        // Flip has_movements to TRUE to activate DB immutability triggers.
        await conn.ExecuteAsync(
            "UPDATE operational_registers SET has_movements = TRUE WHERE register_id = @Id;",
            new { Id = regId });

        // Forbidden: identifier mutation.
        await AssertForbiddenAsync(() => conn.ExecuteAsync(
            """
            UPDATE operational_register_resources
               SET code = 'AMOUNT2', code_norm = 'amount2', column_code = 'amount2'
             WHERE register_id = @RegId AND column_code = 'amount';
            """,
            new { RegId = regId }));

        // Forbidden: delete.
        await AssertForbiddenAsync(() => conn.ExecuteAsync(
            "DELETE FROM operational_register_resources WHERE register_id = @RegId AND column_code = 'amount';",
            new { RegId = regId }));

        // Allowed: user-facing updates.
        await conn.ExecuteAsync(
            """
            UPDATE operational_register_resources
               SET name = 'Amount (renamed)', ordinal = 20
             WHERE register_id = @RegId AND column_code = 'amount';
            """,
            new { RegId = regId });

        var row = await conn.QuerySingleAsync<Row>(
            """
            SELECT code AS "Code", code_norm AS "CodeNorm", column_code AS "ColumnCode", name AS "Name", ordinal AS "Ordinal"
              FROM operational_register_resources
             WHERE register_id = @RegId AND column_code = 'amount';
            """,
            new { RegId = regId });

        row.Code.Should().Be("AMOUNT");
        row.CodeNorm.Should().Be("amount");
        row.ColumnCode.Should().Be("amount");
        row.Name.Should().Be("Amount (renamed)");
        row.Ordinal.Should().Be(20);

        // Allowed: forward-only evolution (new resource).
        await conn.ExecuteAsync(
            """
            INSERT INTO operational_register_resources(register_id, code, code_norm, column_code, name, ordinal)
            VALUES (@RegId, 'QTY', 'qty', 'qty', 'Qty', 30);
            """,
            new { RegId = regId });

        var cnt = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM operational_register_resources WHERE register_id = @RegId;",
            new { RegId = regId });

        cnt.Should().Be(2);
    }

    private static async Task AssertForbiddenAsync(Func<Task> act)
    {
        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().BeOneOf("P0001", "55000");
    }

    private sealed record Row(string Code, string CodeNorm, string ColumnCode, string Name, int Ordinal);
}
