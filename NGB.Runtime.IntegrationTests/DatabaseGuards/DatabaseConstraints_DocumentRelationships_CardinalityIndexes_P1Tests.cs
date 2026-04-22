using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P1: DB-level defense-in-depth for built-in relationship codes.
///
/// Runtime enforces relationship cardinality for all relationship types.
/// Additionally, for a small set of well-known platform codes we add partial unique
/// indexes (see DocumentRelationshipsCardinalityIndexesMigration) to prevent corruption
/// even if application validation is bypassed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_DocumentRelationships_CardinalityIndexes_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime T0 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReversalOf_Max1Outgoing_IsEnforcedByPartialUniqueIndex()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var from = await InsertDraftDocumentAsync(conn, "IT_FROM");
        var to1 = await InsertDraftDocumentAsync(conn, "IT_TO1");
        var to2 = await InsertDraftDocumentAsync(conn, "IT_TO2");

        await InsertRelationshipAsync(conn, from, to1, "reversal_of");

        var act = async () => await InsertRelationshipAsync(conn, from, to2, "reversal_of");

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("ux_docrel_from_rev_of");
    }

    [Fact]
    public async Task CreatedFrom_Max1Outgoing_IsEnforcedByPartialUniqueIndex()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var from = await InsertDraftDocumentAsync(conn, "IT_FROM");
        var to1 = await InsertDraftDocumentAsync(conn, "IT_TO1");
        var to2 = await InsertDraftDocumentAsync(conn, "IT_TO2");

        await InsertRelationshipAsync(conn, from, to1, "created_from");

        var act = async () => await InsertRelationshipAsync(conn, from, to2, "created_from");

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("ux_docrel_from_created_from");
    }

    [Fact]
    public async Task Supersedes_IsOneToOne_BothSidesAreEnforcedByPartialUniqueIndexes()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var from1 = await InsertDraftDocumentAsync(conn, "IT_FROM1");
        var from2 = await InsertDraftDocumentAsync(conn, "IT_FROM2");

        var to1 = await InsertDraftDocumentAsync(conn, "IT_TO1");
        var to2 = await InsertDraftDocumentAsync(conn, "IT_TO2");

        // Outgoing uniqueness: each document can supersede at most one other document.
        await InsertRelationshipAsync(conn, from1, to1, "supersedes");

        var dupOutgoing = async () => await InsertRelationshipAsync(conn, from1, to2, "supersedes");

        var exOut = await dupOutgoing.Should().ThrowAsync<PostgresException>();
        exOut.Which.SqlState.Should().Be("23505");
        exOut.Which.ConstraintName.Should().Be("ux_docrel_from_supersedes");

        // Incoming uniqueness: each document can be superseded by at most one other document.
        await InsertRelationshipAsync(conn, from2, to2, "supersedes");

        // NOTE: We need a valid FROM document id for FK. Create it.
        var from3 = await InsertDraftDocumentAsync(conn, "IT_FROM3");

        var dupIncoming = async () => await InsertRelationshipAsync(conn, from3, to2, "supersedes");

        var exIn = await dupIncoming.Should().ThrowAsync<PostgresException>();
        exIn.Which.SqlState.Should().Be("23505");
        exIn.Which.ConstraintName.Should().Be("ux_docrel_to_supersedes");
    }

    [Fact]
    public async Task PartialIndexes_DoNotAffectOtherRelationshipCodes_MultipleOutgoingAllowed()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var from = await InsertDraftDocumentAsync(conn, "IT_FROM");
        var to1 = await InsertDraftDocumentAsync(conn, "IT_TO1");
        var to2 = await InsertDraftDocumentAsync(conn, "IT_TO2");

        // "based_on" is not part of built-in partial unique indexes.
        await InsertRelationshipAsync(conn, from, to1, "based_on");
        await InsertRelationshipAsync(conn, from, to2, "based_on");

        var count = await conn.ExecuteScalarAsync<int>(
            "select count(*) from document_relationships where from_document_id=@id and relationship_code_norm='based_on'",
            new { id = from });

        count.Should().Be(2);
    }

    private static async Task<Guid> InsertDraftDocumentAsync(NpgsqlConnection conn, string typeCode)
    {
        var id = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            """
            INSERT INTO documents(
                id, type_code, number, date_utc,
                status, posted_at_utc, marked_for_deletion_at_utc,
                created_at_utc, updated_at_utc
            ) VALUES (
                @Id, @TypeCode, NULL, @DateUtc,
                1, NULL, NULL,
                @NowUtc, @NowUtc
            );
            """,
            new
            {
                Id = id,
                TypeCode = typeCode,
                DateUtc = T0,
                NowUtc = T0
            });

        return id;
    }

    private static Task InsertRelationshipAsync(NpgsqlConnection conn, Guid from, Guid to, string relationshipCode)
        => conn.ExecuteAsync(
            """
            INSERT INTO document_relationships(
                relationship_id,
                from_document_id,
                to_document_id,
                relationship_code
            ) VALUES (
                @Id,
                @FromId,
                @ToId,
                @Code
            );
            """,
            new
            {
                Id = Guid.CreateVersion7(),
                FromId = from,
                ToId = to,
                Code = relationshipCode
            });
}
