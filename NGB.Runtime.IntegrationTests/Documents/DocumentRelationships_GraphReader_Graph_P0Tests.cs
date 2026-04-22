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
public sealed class DocumentRelationships_GraphReader_Graph_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Graph_BfsDepthAndFilters_Work()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var docs = sp.GetRequiredService<IDocumentRepository>();
        var relRepo = sp.GetRequiredService<IDocumentRelationshipRepository>();
        var graph = sp.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var d = Guid.CreateVersion7();
        var e = Guid.CreateVersion7();

        var nowUtc = new DateTime(2026, 1, 26, 0, 0, 0, DateTimeKind.Utc);

        static DocumentRecord Doc(Guid id, string type, string number, DateTime nowUtc) => new()
        {
            Id = id,
            TypeCode = type,
            Number = number,
            DateUtc = new DateTime(2026, 1, 26, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        };

        const string basedOn = "based_on";
        const string reversalOf = "reversal_of";
        var basedOnNorm = basedOn;
        var reversalNorm = reversalOf;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(Doc(a, "it_alpha", "A-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(b, "it_beta", "B-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(c, "it_beta", "C-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(d, "it_beta", "D-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(e, "it_beta", "E-0001", nowUtc), ct);

            // A -> B -> E, A -> C, D -> A
            await relRepo.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = DeterministicGuid.Create($"DocumentRelationship|{a:D}|{basedOnNorm}|{b:D}"),
                FromDocumentId = a,
                ToDocumentId = b,
                RelationshipCode = basedOn,
                RelationshipCodeNorm = basedOnNorm,
                CreatedAtUtc = nowUtc.AddSeconds(1)
            }, ct);

            await relRepo.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = DeterministicGuid.Create($"DocumentRelationship|{b:D}|{basedOnNorm}|{e:D}"),
                FromDocumentId = b,
                ToDocumentId = e,
                RelationshipCode = basedOn,
                RelationshipCodeNorm = basedOnNorm,
                CreatedAtUtc = nowUtc.AddSeconds(2)
            }, ct);

            await relRepo.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = DeterministicGuid.Create($"DocumentRelationship|{a:D}|{basedOnNorm}|{c:D}"),
                FromDocumentId = a,
                ToDocumentId = c,
                RelationshipCode = basedOn,
                RelationshipCodeNorm = basedOnNorm,
                CreatedAtUtc = nowUtc.AddSeconds(3)
            }, ct);

            await relRepo.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = DeterministicGuid.Create($"DocumentRelationship|{d:D}|{reversalNorm}|{a:D}"),
                FromDocumentId = d,
                ToDocumentId = a,
                RelationshipCode = reversalOf,
                RelationshipCodeNorm = reversalNorm,
                CreatedAtUtc = nowUtc.AddSeconds(4)
            }, ct);
        }, CancellationToken.None);

        // Depth 2, both directions => should include A(0), B/C/D(1), E(2)
        var g = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: a,
            MaxDepth: 2,
            Direction: DocumentRelationshipTraversalDirection.Both,
            RelationshipCodes: null,
            MaxNodes: 100,
            MaxEdges: 100),
            CancellationToken.None);

        g.RootDocumentId.Should().Be(a);
        g.Nodes.Should().ContainSingle(x => x.DocumentId == a && x.Depth == 0);
        g.Nodes.Should().ContainSingle(x => x.DocumentId == b && x.Depth == 1);
        g.Nodes.Should().ContainSingle(x => x.DocumentId == c && x.Depth == 1);
        g.Nodes.Should().ContainSingle(x => x.DocumentId == d && x.Depth == 1);
        g.Nodes.Should().ContainSingle(x => x.DocumentId == e && x.Depth == 2);
        g.Edges.Should().HaveCount(4);

        // Filter: only 'based_on' => D is not reachable.
        var gf = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: a,
            MaxDepth: 2,
            Direction: DocumentRelationshipTraversalDirection.Both,
            RelationshipCodes: new[] { "  BASED_ON  " },
            MaxNodes: 100,
            MaxEdges: 100),
            CancellationToken.None);

        gf.Nodes.Select(n => n.DocumentId).Should().NotContain(d);
        gf.Edges.All(x => x.RelationshipCodeNorm == "based_on").Should().BeTrue();
        gf.Nodes.Should().ContainSingle(x => x.DocumentId == e && x.Depth == 2);
    }
}
