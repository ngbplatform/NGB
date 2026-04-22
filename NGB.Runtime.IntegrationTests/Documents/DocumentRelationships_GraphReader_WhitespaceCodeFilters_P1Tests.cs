using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_GraphReader_WhitespaceCodeFilters_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OutgoingPage_WhenRelationshipCodeIsWhitespace_TreatsAsNoFilter_ReturnsAllCodes()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var rels = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipRepository>();
        var reader = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var fromId = Guid.CreateVersion7();
        var to1Id = Guid.CreateVersion7();
        var to2Id = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 4, 0, 0, 0, DateTimeKind.Utc);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(NewDraft(fromId, "it_alpha", "A-0001", nowUtc), ct);
            await docs.CreateAsync(NewDraft(to1Id, "it_beta", "B-0001", nowUtc), ct);
            await docs.CreateAsync(NewDraft(to2Id, "it_beta", "B-0002", nowUtc), ct);

            await rels.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|based_on|{to1Id:D}"),
                FromDocumentId = fromId,
                ToDocumentId = to1Id,
                RelationshipCode = "based_on",
                RelationshipCodeNorm = "based_on",
                CreatedAtUtc = nowUtc
            }, ct);

            await rels.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|created_from|{to2Id:D}"),
                FromDocumentId = fromId,
                ToDocumentId = to2Id,
                RelationshipCode = "created_from",
                RelationshipCodeNorm = "created_from",
                CreatedAtUtc = nowUtc
            }, ct);
        }, CancellationToken.None);

        var page = await reader.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(
                DocumentId: fromId,
                RelationshipCode: " \t ",
                PageSize: 100,
                Cursor: null),
            CancellationToken.None);

        page.Items.Should().HaveCount(2);
        page.Items.Select(x => x.RelationshipCodeNorm)
            .Should().BeEquivalentTo(new[] { "based_on", "created_from" });

        page.Items.Select(x => x.OtherDocument.DocumentId)
            .Should().BeEquivalentTo(new[] { to1Id, to2Id });
    }

    [Fact]
    public async Task Graph_WhenRelationshipCodesContainOnlyWhitespace_TreatsAsNoFilter_ReturnsReachableNodes()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var rels = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipRepository>();
        var reader = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var rootId = Guid.CreateVersion7();
        var childId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 4, 0, 0, 0, DateTimeKind.Utc);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(NewDraft(rootId, "it_alpha", "A-0001", nowUtc), ct);
            await docs.CreateAsync(NewDraft(childId, "it_beta", "B-0001", nowUtc), ct);

            await rels.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = DeterministicGuid.Create($"DocumentRelationship|{rootId:D}|based_on|{childId:D}"),
                FromDocumentId = rootId,
                ToDocumentId = childId,
                RelationshipCode = "based_on",
                RelationshipCodeNorm = "based_on",
                CreatedAtUtc = nowUtc
            }, ct);
        }, CancellationToken.None);

        var graph = await reader.GetGraphAsync(
            new DocumentRelationshipGraphRequest(
                RootDocumentId: rootId,
                MaxDepth: 1,
                Direction: DocumentRelationshipTraversalDirection.Outgoing,
                RelationshipCodes: new[] { "  ", "\t" },
                MaxNodes: 10,
                MaxEdges: 10),
            CancellationToken.None);

        graph.Nodes.Select(n => n.DocumentId).Should().Contain(childId);
        graph.Edges.Should().ContainSingle(e => e.FromDocumentId == rootId && e.ToDocumentId == childId && e.RelationshipCodeNorm == "based_on");
    }

    private static DocumentRecord NewDraft(Guid id, string typeCode, string number, DateTime nowUtc)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            Number = number,
            DateUtc = nowUtc,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        };
}
