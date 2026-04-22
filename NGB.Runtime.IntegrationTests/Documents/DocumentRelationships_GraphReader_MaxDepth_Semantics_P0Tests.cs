using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_GraphReader_MaxDepth_Semantics_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetGraphAsync_MaxDepth_BoundsBfsNeighborhood_SemanticsAreStable()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var d = Guid.CreateVersion7();

        await SeedDraftDocsAsync(host, a, b, c, d);

        // A -> B -> C -> D
        await CreateEdgeAsync(host, a, b, "based_on");
        await CreateEdgeAsync(host, b, c, "based_on");
        await CreateEdgeAsync(host, c, d, "based_on");

        await using var scope = host.Services.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        // MaxDepth = 0 => only root, no edges
        var g0 = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: a,
            MaxDepth: 0,
            Direction: DocumentRelationshipTraversalDirection.Outgoing,
            RelationshipCodes: new[] { "based_on" },
            MaxNodes: 100,
            MaxEdges: 100), CancellationToken.None);

        g0.RootDocumentId.Should().Be(a);
        g0.Nodes.Should().ContainSingle(n => n.DocumentId == a && n.Depth == 0);
        g0.Edges.Should().BeEmpty();

        // MaxDepth = 1 => root + direct neighbors
        var g1 = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: a,
            MaxDepth: 1,
            Direction: DocumentRelationshipTraversalDirection.Outgoing,
            RelationshipCodes: new[] { "based_on" },
            MaxNodes: 100,
            MaxEdges: 100), CancellationToken.None);

        g1.Nodes.Select(n => (n.DocumentId, n.Depth))
            .Should().BeEquivalentTo(new[]
            {
                (a, 0),
                (b, 1)
            });

        g1.Edges.Should().ContainSingle(e => e.FromDocumentId == a && e.ToDocumentId == b && e.RelationshipCodeNorm == "based_on");

        // MaxDepth = 2 => includes next hop (C), but not D
        var g2 = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: a,
            MaxDepth: 2,
            Direction: DocumentRelationshipTraversalDirection.Outgoing,
            RelationshipCodes: new[] { "based_on" },
            MaxNodes: 100,
            MaxEdges: 100), CancellationToken.None);

        g2.Nodes.Select(n => (n.DocumentId, n.Depth))
            .Should().BeEquivalentTo(new[]
            {
                (a, 0),
                (b, 1),
                (c, 2)
            });

        g2.Edges.Should().HaveCount(2);
        g2.Edges.Should().Contain(e => e.FromDocumentId == a && e.ToDocumentId == b);
        g2.Edges.Should().Contain(e => e.FromDocumentId == b && e.ToDocumentId == c);
        g2.Edges.Should().NotContain(e => e.FromDocumentId == c && e.ToDocumentId == d);

        // Direction = Both: root B at depth=0 has A and C at depth=1 via incoming/outgoing edges.
        var gBoth1 = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: b,
            MaxDepth: 1,
            Direction: DocumentRelationshipTraversalDirection.Both,
            RelationshipCodes: new[] { "based_on" },
            MaxNodes: 100,
            MaxEdges: 100), CancellationToken.None);

        gBoth1.Nodes.Select(n => (n.DocumentId, n.Depth))
            .Should().BeEquivalentTo(new[]
            {
                (b, 0),
                (a, 1),
                (c, 1)
            });

        gBoth1.Edges.Should().HaveCount(2);
        gBoth1.Edges.Should().Contain(e => e.FromDocumentId == a && e.ToDocumentId == b);
        gBoth1.Edges.Should().Contain(e => e.FromDocumentId == b && e.ToDocumentId == c);
    }

    private static async Task SeedDraftDocsAsync(IHost host, params Guid[] ids)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            for (var i = 0; i < ids.Length; i++)
            {
                await repo.CreateAsync(new DocumentRecord
                {
                    Id = ids[i],
                    TypeCode = "it.doc",
                    Number = $"IT-{i + 1}",
                    DateUtc = NowUtc,
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = NowUtc,
                    UpdatedAtUtc = NowUtc,
                    PostedAtUtc = null,
                    MarkedForDeletionAtUtc = null
                }, ct);
            }
        }, CancellationToken.None);
    }

    private static async Task CreateEdgeAsync(IHost host, Guid fromId, Guid toId, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        await svc.CreateAsync(fromId, toId, code, manageTransaction: true, ct: CancellationToken.None);
    }
}
