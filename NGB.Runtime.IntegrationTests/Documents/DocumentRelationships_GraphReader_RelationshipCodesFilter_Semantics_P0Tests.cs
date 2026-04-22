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
public sealed class DocumentRelationships_GraphReader_RelationshipCodesFilter_Semantics_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetGraphAsync_WhenRelationshipCodesSpecified_TraversesOnlyThoseEdges()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();

        await SeedDraftDocsAsync(host, a, b, c);

        // A -(based_on)-> B
        await CreateEdgeAsync(host, a, b, "based_on");

        // B <-> C (bidirectional)
        await CreateEdgeAsync(host, b, c, "related_to");

        await using var scope = host.Services.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var onlyBasedOn = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: a,
            RelationshipCodes: ["based_on"],
            Direction: DocumentRelationshipTraversalDirection.Outgoing,
            MaxDepth: 5,
            MaxNodes: 100,
            MaxEdges: 100),
            CancellationToken.None);

        onlyBasedOn.RootDocumentId.Should().Be(a);

        // Should include only A(depth0) and B(depth1); C must be unreachable when filtering to based_on only.
        onlyBasedOn.Nodes.Select(n => (n.DocumentId, n.Depth))
            .Should().BeEquivalentTo([
                (a, 0),
                (b, 1)
            ]);

        onlyBasedOn.Edges.Should().ContainSingle();
        onlyBasedOn.Edges.Single().RelationshipCodeNorm.Should().Be("based_on");
        onlyBasedOn.Edges.Single().FromDocumentId.Should().Be(a);
        onlyBasedOn.Edges.Single().ToDocumentId.Should().Be(b);

        // Sanity: if we include both codes, C must become reachable.
        var withBoth = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: a,
            RelationshipCodes: new[] { "based_on", "related_to" },
            Direction: DocumentRelationshipTraversalDirection.Outgoing,
            MaxDepth: 5,
            MaxNodes: 100,
            MaxEdges: 100),
            CancellationToken.None);

        withBoth.Nodes.Should().Contain(n => n.DocumentId == c, "including related_to should allow traversal from B to C");
        withBoth.Edges.Should().Contain(e => e.RelationshipCodeNorm == "related_to");
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

    private static async Task CreateEdgeAsync(IHost host, Guid fromId, Guid toId, string relationshipCode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        await svc.CreateAsync(fromId, toId, relationshipCode, manageTransaction: true, ct: CancellationToken.None);
    }
}
