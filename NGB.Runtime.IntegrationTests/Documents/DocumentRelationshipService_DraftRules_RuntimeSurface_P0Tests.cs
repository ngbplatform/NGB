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
public sealed class DocumentRelationshipService_DraftRules_RuntimeSurface_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateAndDelete_BasedOn_FailFast_WhenFromDocumentIsNotDraft()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();

        await SeedDocsAsync(host, fromId, toId);
        await SetStatusAsync(host, fromId, DocumentStatus.Posted);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            Func<Task> create = () => svc.CreateAsync(fromId, toId, relationshipCode: "based_on", manageTransaction: true, ct: CancellationToken.None);
            var ex = await create.Should().ThrowAsync<DocumentRelationshipValidationException>();
            ex.Which.Reason.Should().Be("from_document_must_be_draft");

            Func<Task> delete = () => svc.DeleteAsync(fromId, toId, relationshipCode: "based_on", manageTransaction: true, ct: CancellationToken.None);
            ex = await delete.Should().ThrowAsync<DocumentRelationshipValidationException>();
            ex.Which.Reason.Should().Be("from_document_must_be_draft");
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_relationships WHERE from_document_id=@from AND to_document_id=@to AND relationship_code_norm='based_on';",
            new { from = fromId, to = toId });

        count.Should().Be(0, "relationship must not be created when from-document is not Draft");
    }

    [Fact]
    public async Task CreateAndDelete_RelatedTo_FailFast_WhenToDocumentIsNotDraft_BecauseBidirectionalRequiresBothDraft()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        await SeedDocsAsync(host, a, b);
        await SetStatusAsync(host, b, DocumentStatus.Posted);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            Func<Task> create = () => svc.CreateAsync(a, b, relationshipCode: "related_to", manageTransaction: true, ct: CancellationToken.None);
            var ex = await create.Should().ThrowAsync<DocumentRelationshipValidationException>();
            ex.Which.Reason.Should().Be("bidirectional_requires_both_draft");

            Func<Task> delete = () => svc.DeleteAsync(a, b, relationshipCode: "related_to", manageTransaction: true, ct: CancellationToken.None);
            ex = await delete.Should().ThrowAsync<DocumentRelationshipValidationException>();
            ex.Which.Reason.Should().Be("bidirectional_requires_both_draft");
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_relationships WHERE relationship_code_norm='related_to';");

        count.Should().Be(0);
    }

    [Fact]
    public async Task BasedOn_AllowsLinkToPosted_ToDocument_WhenFromIsDraft()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();

        await SeedDocsAsync(host, fromId, toId);
        await SetStatusAsync(host, toId, DocumentStatus.Posted);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            (await svc.CreateAsync(fromId, toId, relationshipCode: "based_on", manageTransaction: true, ct: CancellationToken.None))
                .Should().BeTrue();
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_relationships WHERE from_document_id=@from AND to_document_id=@to AND relationship_code_norm='based_on';",
            new { from = fromId, to = toId });

        count.Should().Be(1);
    }

    private static async Task SeedDocsAsync(IHost host, Guid a, Guid b)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = a,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = NowUtc.Date,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await docs.CreateAsync(new DocumentRecord
            {
                Id = b,
                TypeCode = "it_beta",
                Number = "B-0001",
                DateUtc = NowUtc.Date,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);
    }

    private static async Task SetStatusAsync(IHost host, Guid documentId, DocumentStatus status)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.UpdateStatusAsync(
                documentId: documentId,
                status: status,
                updatedAtUtc: NowUtc,
                postedAtUtc: status == DocumentStatus.Posted ? NowUtc : null,
                markedForDeletionAtUtc: status == DocumentStatus.MarkedForDeletion ? NowUtc : null,
                ct);
        }, CancellationToken.None);
    }
}
