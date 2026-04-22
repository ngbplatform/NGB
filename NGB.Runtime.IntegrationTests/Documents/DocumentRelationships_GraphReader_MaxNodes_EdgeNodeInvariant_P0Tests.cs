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
public sealed class DocumentRelationships_GraphReader_MaxNodes_EdgeNodeInvariant_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Graph_WhenMaxNodesIsReached_DoesNotReturnEdgesToMissingNodes()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var docs = sp.GetRequiredService<IDocumentRepository>();
        var relRepo = sp.GetRequiredService<IDocumentRelationshipRepository>();
        var graph = sp.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var root = Guid.CreateVersion7();
        var children = Enumerable.Range(1, 6).Select(_ => Guid.CreateVersion7()).ToArray();
        var expectedIncludedChild = children[^1];

        var nowUtc = new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc);

        static DocumentRecord Doc(Guid id, string type, string number, DateTime nowUtc) => new()
        {
            Id = id,
            TypeCode = type,
            Number = number,
            DateUtc = new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        };

        const string basedOn = "based_on";
        var basedOnNorm = basedOn;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(Doc(root, "it_root", "R-0001", nowUtc), ct);

            for (var i = 0; i < children.Length; i++)
            {
                var childId = children[i];
                await docs.CreateAsync(Doc(childId, "it_child", $"C-{i + 1:0000}", nowUtc), ct);

                await relRepo.TryCreateAsync(new DocumentRelationshipRecord
                {
                    Id = DeterministicGuid.Create($"DocumentRelationship|{root:D}|{basedOnNorm}|{childId:D}"),
                    FromDocumentId = root,
                    ToDocumentId = childId,
                    RelationshipCode = basedOn,
                    RelationshipCodeNorm = basedOnNorm,
                    CreatedAtUtc = nowUtc.AddSeconds(i + 1)
                }, ct);
            }
        }, CancellationToken.None);

        var g = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: root,
            MaxDepth: 1,
            Direction: DocumentRelationshipTraversalDirection.Outgoing,
            RelationshipCodes: null,
            MaxNodes: 2,
            MaxEdges: 100),
            CancellationToken.None);

        g.Nodes.Should().HaveCount(2);
        g.Nodes.Should().ContainSingle(x => x.DocumentId == root && x.Depth == 0);
        g.Nodes.Should().ContainSingle(x => x.DocumentId == expectedIncludedChild && x.Depth == 1);

        // With MaxNodes=2 (root + 1 child), the graph must not include edges to other children.
        g.Edges.Should().HaveCount(1);

        var nodeIds = g.Nodes.Select(x => x.DocumentId).ToHashSet();
        g.Edges.All(e => nodeIds.Contains(e.FromDocumentId) && nodeIds.Contains(e.ToDocumentId)).Should().BeTrue();
    }
}
