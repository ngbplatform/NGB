using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_NoOp_And_FailFast_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PostAsync_WhenAlreadyPosted_IsStrictNoOp_DoesNotInvokePostingAction_AndNoSideEffects()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var docId = Guid.CreateVersion7();
        await CreateDocumentAsync(host, new DocumentRecord
        {
            Id = docId,
            TypeCode = "it.noop.post",
            Number = "NOOP-POST-1",
            DateUtc = NowUtc,
            Status = DocumentStatus.Posted,
            CreatedAtUtc = NowUtc,
            UpdatedAtUtc = NowUtc,
            PostedAtUtc = NowUtc,
            MarkedForDeletionAtUtc = null
        });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            // If called, this must fail the test.
            var act = () => posting.PostAsync(docId, (_, __) =>
                throw new XunitException("postingAction must not be invoked for already Posted document"),
                CancellationToken.None);

            await act.Should().NotThrowAsync("Post on already Posted document must be strict no-op");
        }

        await AssertNoSideEffectsAsync(docId, expectedStatus: DocumentStatus.Posted, expectedPostedAtUtc: NowUtc, expectedUpdatedAtUtc: NowUtc);
    }

    [Fact]
    public async Task UnpostAsync_WhenDraft_IsStrictNoOp_AndNoSideEffects()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var docId = Guid.CreateVersion7();
        await CreateDocumentAsync(host, new DocumentRecord
        {
            Id = docId,
            TypeCode = "it.noop.unpost",
            Number = "NOOP-UNPOST-1",
            DateUtc = NowUtc,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = NowUtc,
            UpdatedAtUtc = NowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            var act = () => posting.UnpostAsync(docId, CancellationToken.None);

            await act.Should().NotThrowAsync("Unpost on Draft document must be strict no-op");
        }

        await AssertNoSideEffectsAsync(docId, expectedStatus: DocumentStatus.Draft, expectedPostedAtUtc: null, expectedUpdatedAtUtc: NowUtc);
    }

    [Fact]
    public async Task PostAsync_WhenMarkedForDeletion_Throws_AndDoesNotWriteAnything()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var docId = Guid.CreateVersion7();
        await CreateDocumentAsync(host, new DocumentRecord
        {
            Id = docId,
            TypeCode = "it.failfast.post",
            Number = "FAILFAST-POST-1",
            DateUtc = NowUtc,
            Status = DocumentStatus.MarkedForDeletion,
            CreatedAtUtc = NowUtc,
            UpdatedAtUtc = NowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = NowUtc
        });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            var act = () => posting.PostAsync(docId, (_, __) => Task.CompletedTask, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<DocumentMarkedForDeletionException>();
            ex.Which.ErrorCode.Should().Be(DocumentMarkedForDeletionException.ErrorCodeConst);
            ex.Which.Context["operation"].Should().Be("Document.Post");
            ex.Which.Context["documentId"].Should().Be(docId);
        }

        // status & timestamps must remain unchanged, and no side effects are allowed.
        await AssertNoSideEffectsAsync(docId, expectedStatus: DocumentStatus.MarkedForDeletion, expectedPostedAtUtc: null, expectedUpdatedAtUtc: NowUtc);
    }

    private static async Task CreateDocumentAsync(IHost host, DocumentRecord record)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(record, ct);
        }, CancellationToken.None);
    }

    private async Task AssertNoSideEffectsAsync(Guid documentId, DocumentStatus expectedStatus, DateTime? expectedPostedAtUtc, DateTime expectedUpdatedAtUtc)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var doc = await conn.QuerySingleAsync<(short status, DateTime updated_at_utc, DateTime? posted_at_utc)>(
            "SELECT status, updated_at_utc, posted_at_utc FROM documents WHERE id = @id;",
            new { id = documentId });

        ((DocumentStatus)doc.status).Should().Be(expectedStatus);
        doc.updated_at_utc.Should().Be(expectedUpdatedAtUtc);
        doc.posted_at_utc.Should().Be(expectedPostedAtUtc);

        // Posting side effects must not exist.
        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @id;",
            new { id = documentId }))
            .Should().Be(0);

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @id;",
            new { id = documentId }))
            .Should().Be(0);

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM operational_register_write_state WHERE document_id = @id;",
            new { id = documentId }))
            .Should().Be(0);

        // Audit must not be written on no-op / fail-fast paths.
        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events WHERE entity_id = @id;",
            new { id = documentId }))
            .Should().Be(0);

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_event_changes c " +
            "JOIN platform_audit_events e ON e.audit_event_id = c.audit_event_id " +
            "WHERE e.entity_id = @id;",
            new { id = documentId }))
            .Should().Be(0);
    }
}
