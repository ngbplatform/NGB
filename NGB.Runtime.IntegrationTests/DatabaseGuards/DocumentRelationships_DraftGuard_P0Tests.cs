using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_DraftGuard_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task InsertAndDelete_AreForbidden_WhenFromDocumentIsNotDraft()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        // Create docs
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await docs.CreateAsync(new DocumentRecord
            {
                Id = toId,
                TypeCode = "it_beta",
                Number = "B-0001",
                DateUtc = new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        const string code = "based_on";
        var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{code}|{toId:D}");

        // Create relationship while Draft (allowed).
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            await conn.ExecuteAsync(
                """
                INSERT INTO document_relationships (relationship_id, from_document_id, to_document_id, relationship_code, created_at_utc)
                VALUES (@Id, @From, @To, @Code, @At);
                """,
                new { Id = relId, From = fromId, To = toId, Code = code, At = nowUtc });
        }

        // Post the "from" document
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.UpdateStatusAsync(
                documentId: fromId,
                status: DocumentStatus.Posted,
                updatedAtUtc: nowUtc,
                postedAtUtc: nowUtc,
                markedForDeletionAtUtc: null,
                ct);
        }, CancellationToken.None);

        // INSERT must be rejected by DB trigger.
        const string code2 = "see_also";
        var relId2 = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{code2}|{toId:D}");

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            await using var tx = await conn.BeginTransactionAsync(CancellationToken.None);

            var act = async () =>
            {
                const string sql = """
                                   INSERT INTO document_relationships
                                   (relationship_id, from_document_id, to_document_id, relationship_code, created_at_utc)
                                   VALUES
                                   (@Id, @From, @To, @Code, @At);
                                   """;
                await conn.ExecuteAsync(sql, new { Id = relId2, From = fromId, To = toId, Code = code2, At = nowUtc }, tx);
            };

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("55000");
            await tx.RollbackAsync(CancellationToken.None);
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            await using var tx = await conn.BeginTransactionAsync(CancellationToken.None);

            var act = async () =>
            {
                await conn.ExecuteAsync(
                    "DELETE FROM document_relationships WHERE relationship_id = @Id;",
                    new { Id = relId },
                    tx);
            };

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("55000");
            await tx.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task CascadeDelete_IsAllowed_EvenIfFromDocumentIsPosted()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await docs.CreateAsync(new DocumentRecord
            {
                Id = toId,
                TypeCode = "it_beta",
                Number = "B-0001",
                DateUtc = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        const string code = "based_on";
        var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{code}|{toId:D}");

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            await conn.ExecuteAsync(
                """
                INSERT INTO document_relationships (relationship_id, from_document_id, to_document_id, relationship_code, created_at_utc)
                VALUES (@Id, @From, @To, @Code, @At);
                """,
                new { Id = relId, From = fromId, To = toId, Code = code, At = nowUtc });
        }

        // Post the from-document (relationships become immutable by business semantics)
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.UpdateStatusAsync(
                documentId: fromId,
                status: DocumentStatus.Posted,
                updatedAtUtc: nowUtc,
                postedAtUtc: nowUtc,
                markedForDeletionAtUtc: null,
                ct);
        }, CancellationToken.None);

        // Deleting the "to" document should cascade-delete relationship row, even though from is posted.
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            (await docs.TryDeleteAsync(toId, ct)).Should().BeTrue();
        }, CancellationToken.None);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM document_relationships WHERE relationship_id = @Id;",
                new { Id = relId });
            count.Should().Be(0);
        }
    }
}
