using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.UnitOfWork;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: DB-level constraints on <c>document_relationships</c> must be enforced.
/// We already have runtime and DB draft-guard tests; this suite focuses on relational invariants:
/// - relationship_code trimmed / non-empty / max length
/// - from != to
/// - FK to documents (both ends)
/// - unique triplet (from, relationship_code_norm, to) with case-insensitive normalization
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_Constraints_Enforced_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RelationshipCode_CheckConstraints_AreEnforced()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        await SeedDraftDocsAsync(host, fromId, toId);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // 1) Trimmed constraint
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO document_relationships (relationship_id, from_document_id, to_document_id, relationship_code)
                    VALUES (@id, @from, @to, @code);
                    """,
                    new
                    {
                        id = Guid.CreateVersion7(),
                        from = fromId,
                        to = toId,
                        code = " based_on "
                    }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23514");
            ex.Which.Message.Should().Contain("ck_document_relationships_code_trimmed");
        }

        // 2) Non-empty constraint (empty string passes trimmed check but must fail non-empty check)
        {
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO document_relationships (relationship_id, from_document_id, to_document_id, relationship_code)
                    VALUES (@id, @from, @to, @code);
                    """,
                    new
                    {
                        id = Guid.CreateVersion7(),
                        from = fromId,
                        to = toId,
                        code = ""
                    }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23514");
            ex.Which.Message.Should().Contain("ck_document_relationships_code_nonempty");
        }

        // 3) Length constraint
        {
            var longCode = new string('a', 129);
            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO document_relationships (relationship_id, from_document_id, to_document_id, relationship_code)
                    VALUES (@id, @from, @to, @code);
                    """,
                    new
                    {
                        id = Guid.CreateVersion7(),
                        from = fromId,
                        to = toId,
                        code = longCode
                    }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23514");
            ex.Which.Message.Should().Contain("ck_document_relationships_code_len");
        }
    }

    [Fact]
    public async Task NotSelf_UniqueTriplet_And_ForeignKeys_AreEnforced()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        await SeedDraftDocsAsync(host, fromId, toId);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // 1) not-self
        {
            var selfId = Guid.CreateVersion7();
            await SeedDraftDocsAsync(host, selfId, selfId);

            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO document_relationships (relationship_id, from_document_id, to_document_id, relationship_code)
                    VALUES (@id, @from, @to, @code);
                    """,
                    new
                    {
                        id = Guid.CreateVersion7(),
                        from = selfId,
                        to = selfId,
                        code = "derived_from"
                    }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23514");
            ex.Which.Message.Should().Contain("ck_document_relationships_not_self");
        }

        // 2) FK to_document_id
        {
            var missingTo = Guid.CreateVersion7();

            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO document_relationships (relationship_id, from_document_id, to_document_id, relationship_code)
                    VALUES (@id, @from, @to, @code);
                    """,
                    new
                    {
                        id = Guid.CreateVersion7(),
                        from = fromId,
                        to = missingTo,
                        code = "derived_from"
                    }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23503");
            ex.Which.Message.Should().Contain("fk_document_relationships_to_document");
        }

        // 3) Unique triplet with case-insensitive normalization
        {
            var firstId = Guid.CreateVersion7();
            await conn.ExecuteAsync(
                """
                INSERT INTO document_relationships (relationship_id, from_document_id, to_document_id, relationship_code)
                VALUES (@id, @from, @to, @code);
                """,
                new
                {
                    id = firstId,
                    from = fromId,
                    to = toId,
                    code = "Based_On"
                });

            var ex = await FluentActions
                .Invoking(() => conn.ExecuteAsync(
                    """
                    INSERT INTO document_relationships (relationship_id, from_document_id, to_document_id, relationship_code)
                    VALUES (@id, @from, @to, @code);
                    """,
                    new
                    {
                        id = Guid.CreateVersion7(),
                        from = fromId,
                        to = toId,
                        code = "based_on"
                    }))
                .Should().ThrowAsync<PostgresException>();

            ex.Which.SqlState.Should().Be("23505");
            ex.Which.Message.Should().Contain("ux_document_relationships_triplet");
        }
    }

    private static async Task SeedDraftDocsAsync(IHost host, Guid fromId, Guid toId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var dateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            async Task EnsureDraftAsync(Guid id)
            {
                var existing = await docs.GetAsync(id, ct);
                if (existing is not null)
                    return;

                await docs.CreateAsync(new DocumentRecord
                {
                    Id = id,
                    TypeCode = "it_doc",
                    Number = null,
                    DateUtc = dateUtc,
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                    PostedAtUtc = null,
                    MarkedForDeletionAtUtc = null
                }, ct);
            }

            await EnsureDraftAsync(fromId);
            await EnsureDraftAsync(toId);
        }, CancellationToken.None);
    }
}
