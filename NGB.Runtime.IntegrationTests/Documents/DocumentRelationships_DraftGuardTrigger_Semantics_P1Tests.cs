using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

/// <summary>
/// P1: Defense in depth - the document_relationships draft-guard trigger enforces key invariants at the SQL level:
/// - UPDATE is forbidden (edges are immutable).
/// - Direct DELETE is forbidden unless the from-document is Draft.
/// - FK cascades (document deletions) must be allowed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_DraftGuardTrigger_Semantics_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DraftGuard_ForbidsUpdate_AndForbidsDirectDelete_WhenFromIsNotDraft_ButAllowsFkCascadeDeletes()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();

        var t0 = new DateTime(2026, 2, 4, 0, 0, 0, DateTimeKind.Utc);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = "IT-A-0001",
                DateUtc = t0,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = t0,
                UpdatedAtUtc = t0,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await docs.CreateAsync(new DocumentRecord
            {
                Id = toId,
                TypeCode = "it_beta",
                Number = "IT-B-0001",
                DateUtc = t0,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = t0,
                UpdatedAtUtc = t0,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        (await svc.CreateAsync(fromId, toId, relationshipCode: "based_on", manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        var codeNorm = "based_on";
        var relationshipId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{toId:D}");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        // Flip the from-document to Posted (status=2 requires posted_at_utc != NULL).
        await conn.ExecuteAsync(
            """
            UPDATE documents
               SET status = 2, posted_at_utc = @Now, updated_at_utc = @Now
             WHERE id = @Id;
            """,
            new { Id = fromId, Now = t0.AddMinutes(1) });

        // Sanity: relationship row exists.
        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_relationships WHERE relationship_id = @Id;",
            new { Id = relationshipId }))
            .Should().Be(1);

        // UPDATE on document_relationships is forbidden (immutable edges).
        Func<Task> updateAct = () => conn.ExecuteAsync(
            """
            UPDATE document_relationships
               SET created_at_utc = created_at_utc + interval '1 day'
             WHERE relationship_id = @Id;
            """,
            new { Id = relationshipId });

        var updateEx = await updateAct.Should().ThrowAsync<PostgresException>();
        updateEx.Which.SqlState.Should().Be("55000");
        updateEx.Which.MessageText.Should().Contain("immutable");

        // Direct DELETE is forbidden when from-document is not Draft.
        Func<Task> deleteAct = () => conn.ExecuteAsync(
            "DELETE FROM document_relationships WHERE relationship_id = @Id;",
            new { Id = relationshipId });

        var deleteEx = await deleteAct.Should().ThrowAsync<PostgresException>();
        deleteEx.Which.SqlState.Should().Be("55000");
        deleteEx.Which.MessageText.Should().Contain("from-document").And.Contain("Draft");

        // FK cascade path must be allowed even if from-document is Posted.
        await conn.ExecuteAsync(
            "DELETE FROM documents WHERE id = @Id;",
            new { Id = toId });

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_relationships WHERE relationship_id = @Id;",
            new { Id = relationshipId }))
            .Should().Be(0);
    }
}
