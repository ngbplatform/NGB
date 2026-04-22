using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: DB-level invariants for operational_registers.table_code.
///
/// Even if a register row is inserted/modified outside of runtime services,
/// the database must defend itself against:
/// - empty / invalid table_code (would break per-register table naming)
/// - overlong table_code (would exceed PostgreSQL 63-char identifier limit when used in opreg_* tables)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegister_TableCode_DbGuards_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DbGeneratedTableCode_WhenCodeIsVeryLong_IsTruncatedWithHash_AndFitsIdentifierLimit()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // long (200 chars) -> normalized token will exceed 46, so it must be truncated with a hash suffix.
        var code = new string('A', 200);
        var regId = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            "INSERT INTO operational_registers(register_id, code, name) VALUES (@Id, @Code, 'Long Code');",
            new { Id = regId, Code = code });

        var row = await conn.QuerySingleAsync<Row>(
            "SELECT code_norm AS \"CodeNorm\", table_code AS \"TableCode\" FROM operational_registers WHERE register_id=@Id;",
            new { Id = regId });

        row.CodeNorm.Should().Be(code.Trim().ToLowerInvariant());
        row.TableCode.Should().NotBeNullOrWhiteSpace();
        row.TableCode.Length.Should().BeLessThanOrEqualTo(46);
        row.TableCode.Should().MatchRegex("^[a-z0-9_]+$");
        row.TableCode.Should().MatchRegex("_[0-9a-f]{12}$", "long tokens must be disambiguated by a deterministic hash suffix");

        var movementsTable = $"opreg_{row.TableCode}__movements";
        var turnoversTable = $"opreg_{row.TableCode}__turnovers";
        var balancesTable = $"opreg_{row.TableCode}__balances";

        movementsTable.Length.Should().BeLessThanOrEqualTo(63);
        turnoversTable.Length.Should().BeLessThanOrEqualTo(63);
        balancesTable.Length.Should().BeLessThanOrEqualTo(63);
    }

    [Fact]
    public async Task DbConstraints_WhenCodeNormalizesToEmptyTableCode_RejectsInsert()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // Only separators => normalization would result in an empty token.
        var regId = Guid.CreateVersion7();

        var act = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO operational_registers(register_id, code, name) VALUES (@Id, '---', 'Bad');",
                new { Id = regId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().StartWith("ck_operational_registers_table_code");
    }

    private sealed record Row(string CodeNorm, string TableCode);
}
