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

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_DraftGuard_DbEnforcement_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Insert_WhenFromDocumentIsNotDraft_IsRejectedByDbGuard()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|based_on|{toId:D}");
        var nowUtc = DateTime.UtcNow;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Posted,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = nowUtc,
                MarkedForDeletionAtUtc = null
            }, ct);

            await docs.CreateAsync(new DocumentRecord
            {
                Id = toId,
                TypeCode = "it_beta",
                Number = "B-0001",
                DateUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        // Act: raw SQL INSERT must be rejected by the trigger even if a caller bypasses Runtime services.
        Func<Task> act = async () =>
        {
            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                await uow.EnsureConnectionOpenAsync(ct);
                await uow.Connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into document_relationships(
                        relationship_id,
                        from_document_id,
                        to_document_id,
                        relationship_code,
                        created_at_utc
                    )
                    values (@Id, @FromId, @ToId, @Code, @NowUtc);
                    """,
                    new
                    {
                        Id = relId,
                        FromId = fromId,
                        ToId = toId,
                        Code = "based_on",
                        NowUtc = nowUtc
                    },
                    transaction: uow.Transaction,
                    cancellationToken: ct));
            }, CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("55000");
        ex.Which.MessageText.Should().Contain("from-document").And.Contain("Draft");
    }

    [Fact]
    public async Task Update_IsRejectedByDbGuard()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|based_on|{toId:D}");
        var nowUtc = DateTime.UtcNow;

        // Arrange: insert a valid edge.
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc),
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
                DateUtc = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await uow.EnsureConnectionOpenAsync(ct);
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                insert into document_relationships(
                    relationship_id,
                    from_document_id,
                    to_document_id,
                    relationship_code,
                    created_at_utc
                )
                values (@Id, @FromId, @ToId, @Code, @NowUtc);
                """,
                new
                {
                    Id = relId,
                    FromId = fromId,
                    ToId = toId,
                    Code = "based_on",
                    NowUtc = nowUtc
                },
                transaction: uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        // Act: any UPDATE must be rejected (relationships are immutable edges).
        Func<Task> act = async () =>
        {
            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                await uow.EnsureConnectionOpenAsync(ct);
                await uow.Connection.ExecuteAsync(new CommandDefinition(
                    "update document_relationships set relationship_code = 'something_else' where relationship_id = @Id;",
                    new { Id = relId },
                    transaction: uow.Transaction,
                    cancellationToken: ct));
            }, CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("55000");
        ex.Which.MessageText.Should().Contain("immutable");
    }

    [Fact]
    public async Task Delete_WhenFromDocumentIsNotDraft_IsRejectedForDirectDelete_ButAllowedForFkCascade()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|based_on|{toId:D}");
        var nowUtc = DateTime.UtcNow;

        // Arrange: create docs (both Draft) and the edge.
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
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
                DateUtc = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await uow.EnsureConnectionOpenAsync(ct);
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                insert into document_relationships(
                    relationship_id,
                    from_document_id,
                    to_document_id,
                    relationship_code,
                    created_at_utc
                )
                values (@Id, @FromId, @ToId, @Code, @NowUtc);
                """,
                new
                {
                    Id = relId,
                    FromId = fromId,
                    ToId = toId,
                    Code = "based_on",
                    NowUtc = nowUtc
                },
                transaction: uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        // Make from-document non-Draft (to simulate a posted document).
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.UpdateStatusAsync(
                fromId,
                status: DocumentStatus.Posted,
                updatedAtUtc: nowUtc,
                postedAtUtc: nowUtc,
                markedForDeletionAtUtc: null,
                ct);
        }, CancellationToken.None);

        // Act 1: direct DELETE must be rejected because from-document is not Draft.
        Func<Task> directDelete = async () =>
        {
            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                await uow.EnsureConnectionOpenAsync(ct);
                await uow.Connection.ExecuteAsync(new CommandDefinition(
                    "delete from document_relationships where relationship_id = @Id;",
                    new { Id = relId },
                    transaction: uow.Transaction,
                    cancellationToken: ct));
            }, CancellationToken.None);
        };

        var ex = await directDelete.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("55000");
        ex.Which.MessageText.Should().Contain("from-document").And.Contain("Draft");

        // Act 2: deleting the *to-document* must succeed and cascade-delete the edge.
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            (await docs.TryDeleteAsync(toId, ct)).Should().BeTrue();
        }, CancellationToken.None);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.EnsureConnectionOpenAsync(ct);
            var count = await uow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(*) from document_relationships where relationship_id = @Id;",
                new { Id = relId },
                transaction: uow.Transaction,
                cancellationToken: ct));
            count.Should().Be(0, "FK cascade delete must not be blocked by document_relationships draft guard");
        }, CancellationToken.None);
    }
}
