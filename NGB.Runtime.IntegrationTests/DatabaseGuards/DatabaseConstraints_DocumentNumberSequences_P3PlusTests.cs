using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P3+: Database-level constraints for document_number_sequences.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_DocumentNumberSequences_P3PlusTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DocumentNumberSequences_PrimaryKey_IsEnforcedByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            INSERT INTO document_number_sequences(type_code, fiscal_year, last_seq)
            VALUES ('IT', 2026, 1);
            """);

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO document_number_sequences(type_code, fiscal_year, last_seq)
                VALUES ('IT', 2026, 2);
                """);
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("pk_document_number_sequences");
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(3001)]
    public async Task DocumentNumberSequences_FiscalYearRange_IsEnforcedByDb(int fiscalYear)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO document_number_sequences(type_code, fiscal_year, last_seq)
                VALUES ('IT', @FiscalYear, 1);
                """,
                new { FiscalYear = fiscalYear });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_document_number_sequences_fiscal_year");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task DocumentNumberSequences_LastSeqMustBePositive_IsEnforcedByDb(long lastSeq)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO document_number_sequences(type_code, fiscal_year, last_seq)
                VALUES ('IT', 2026, @LastSeq);
                """,
                new { LastSeq = lastSeq });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_document_number_sequences_last_seq");
    }

    [Fact]
    public async Task DocumentNumberSequences_ValidRow_IsAcceptedByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            INSERT INTO document_number_sequences(type_code, fiscal_year, last_seq)
            VALUES ('IT', 2026, 42);
            """);

        var row = await conn.QuerySingleAsync<(string TypeCode, int FiscalYear, long LastSeq)>(
            """
            SELECT type_code AS TypeCode, fiscal_year AS FiscalYear, last_seq AS LastSeq
            FROM document_number_sequences
            WHERE type_code='IT' AND fiscal_year=2026;
            """);

        row.TypeCode.Should().Be("IT");
        row.FiscalYear.Should().Be(2026);
        row.LastSeq.Should().Be(42);
    }
}
