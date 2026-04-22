using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

/// <summary>
/// P2: The platform-level documents table must persist audit timestamps and
/// allow explicit, transaction-serialized state transitions.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentRepository_AuditTimestamps_AndStateTransitions_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DocumentStatusTransitions_PersistExpectedTimestamps()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var id = Guid.CreateVersion7();
        var t0 = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddMinutes(1);
        var t2 = t0.AddMinutes(2);
        var t3 = t0.AddMinutes(3);

        await CreateDraftDocumentAsync(host, id, t0);

        // Draft -> Posted
        await UpdateStatusInTxnAsync(host, id, DocumentStatus.Posted, updatedAtUtc: t1, postedAtUtc: t1, markedAtUtc: null);
        await AssertStateAsync(host, id,
            expectedStatus: DocumentStatus.Posted,
            expectedCreated: t0,
            expectedUpdated: t1,
            expectedPosted: t1,
            expectedMarked: null);

        // Posted -> MarkedForDeletion
        await UpdateStatusInTxnAsync(host, id, DocumentStatus.MarkedForDeletion, updatedAtUtc: t2, postedAtUtc: null, markedAtUtc: t2);
        await AssertStateAsync(host, id,
            expectedStatus: DocumentStatus.MarkedForDeletion,
            expectedCreated: t0,
            expectedUpdated: t2,
            expectedPosted: null,
            expectedMarked: t2);

        // MarkedForDeletion -> Draft (unmark)
        await UpdateStatusInTxnAsync(host, id, DocumentStatus.Draft, updatedAtUtc: t3, postedAtUtc: null, markedAtUtc: null);
        await AssertStateAsync(host, id,
            expectedStatus: DocumentStatus.Draft,
            expectedCreated: t0,
            expectedUpdated: t3,
            expectedPosted: null,
            expectedMarked: null);
    }

    private static async Task CreateDraftDocumentAsync(IHost host, Guid id, DateTime nowUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.CreateAsync(new DocumentRecord
        {
            Id = id,
            TypeCode = "IT",
            Number = "0001",
            DateUtc = nowUtc,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        }, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task UpdateStatusInTxnAsync(
        IHost host,
        Guid id,
        DocumentStatus status,
        DateTime updatedAtUtc,
        DateTime? postedAtUtc,
        DateTime? markedAtUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        (await repo.GetForUpdateAsync(id, CancellationToken.None)).Should().NotBeNull();

        await repo.UpdateStatusAsync(
            documentId: id,
            status: status,
            updatedAtUtc: updatedAtUtc,
            postedAtUtc: postedAtUtc,
            markedForDeletionAtUtc: markedAtUtc,
            ct: CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task AssertStateAsync(
        IHost host,
        Guid id,
        DocumentStatus expectedStatus,
        DateTime expectedCreated,
        DateTime expectedUpdated,
        DateTime? expectedPosted,
        DateTime? expectedMarked)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var doc = await repo.GetAsync(id, CancellationToken.None);
        doc.Should().NotBeNull();

        doc!.Status.Should().Be(expectedStatus);
        doc.CreatedAtUtc.Should().Be(expectedCreated);
        doc.UpdatedAtUtc.Should().Be(expectedUpdated);
        doc.PostedAtUtc.Should().Be(expectedPosted);
        doc.MarkedForDeletionAtUtc.Should().Be(expectedMarked);
    }
}
