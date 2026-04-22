using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationshipService_Existence_And_DraftRules_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateAsync_WhenFromDocumentDoesNotExist_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var fromId = DeterministicGuid.Create("it_missing_from");
        var toId = DeterministicGuid.Create("it_existing_to");

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(NewDoc(toId, status: DocumentStatus.Draft), ct);
        }, CancellationToken.None);

        Func<Task> act = () => svc.CreateAsync(fromId, toId, relationshipCode: "based_on", manageTransaction: true, ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentNotFoundException>();
        ex.Which.AssertNgbError(DocumentNotFoundException.Code, "documentId");
        ex.Which.DocumentId.Should().Be(fromId);
    }

    [Fact]
    public async Task CreateAsync_WhenToDocumentDoesNotExist_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var fromId = DeterministicGuid.Create("it_existing_from");
        var toId = DeterministicGuid.Create("it_missing_to");

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(NewDoc(fromId, status: DocumentStatus.Draft), ct);
        }, CancellationToken.None);

        Func<Task> act = () => svc.CreateAsync(fromId, toId, relationshipCode: "based_on", manageTransaction: true, ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentNotFoundException>();
        ex.Which.DocumentId.Should().Be(toId);
    }

    [Fact]
    public async Task CreateAsync_WhenFromDocumentIsNotDraft_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var fromId = Guid.NewGuid();
        var toId = Guid.NewGuid();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(NewDoc(fromId, status: DocumentStatus.Posted), ct);
            await docs.CreateAsync(NewDoc(toId, status: DocumentStatus.Draft), ct);
        }, CancellationToken.None);

        Func<Task> act = () => svc.CreateAsync(fromId, toId, relationshipCode: "based_on", manageTransaction: true, ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentRelationshipValidationException>();
        ex.Which.Reason.Should().Be("from_document_must_be_draft");
    }

    [Fact]
    public async Task CreateAsync_BidirectionalRelationship_WhenToDocumentIsNotDraft_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(NewDoc(a, status: DocumentStatus.Draft), ct);
            await docs.CreateAsync(NewDoc(b, status: DocumentStatus.Posted), ct);
        }, CancellationToken.None);

        Func<Task> act = () => svc.CreateAsync(a, b, relationshipCode: "related_to", manageTransaction: true, ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentRelationshipValidationException>();
        ex.Which.Reason.Should().Be("bidirectional_requires_both_draft");
    }

    private static DocumentRecord NewDoc(Guid id, DocumentStatus status)
    {
        var nowUtc = new DateTime(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);
        return new DocumentRecord
        {
            Id = id,
            TypeCode = "it_alpha",
            Number = $"IT-{id.ToString("N")[..8]}",
            DateUtc = new DateTime(2026, 2, 4, 0, 0, 0, DateTimeKind.Utc),
            Status = status,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = status == DocumentStatus.Posted ? nowUtc : null,
            MarkedForDeletionAtUtc = null
        };
    }
}
