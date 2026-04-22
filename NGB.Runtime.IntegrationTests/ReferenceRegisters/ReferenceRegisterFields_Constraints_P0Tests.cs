using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P0: metadata tables must enforce identifier invariants at the DB level
/// (defense-in-depth against accidental raw SQL inserts / buggy writers).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterFields_Constraints_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DbConstraints_PreventReservedColumnCodeWithinRegister()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var regId = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO reference_registers(register_id, code, name, periodicity, record_mode) VALUES (@Id, 'RR', 'RR', 0, 0);",
            new { Id = regId });

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO reference_register_fields(register_id, code, code_norm, column_code, name, ordinal, column_type, is_nullable)
                VALUES (@RegId, 'occurred_at_utc', 'occurred_at_utc', 'occurred_at_utc', 'Bad', 10, 0, TRUE);
                """,
                new { RegId = regId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_reference_register_fields__column_code_not_reserved");
    }
}
