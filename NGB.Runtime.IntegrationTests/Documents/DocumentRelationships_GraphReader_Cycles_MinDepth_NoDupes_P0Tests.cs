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
public sealed class DocumentRelationships_GraphReader_Cycles_MinDepth_NoDupes_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Graph_WithCycle_DoesNotLoop_AndUsesMinDepth_AndDoesNotDuplicateEdges()
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

        var nowUtc = new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc);

        static DocumentRecord Doc(Guid id, string type, string number, DateTime nowUtc) => new()
        {
            Id = id,
            TypeCode = type,
            Number = number,
            DateUtc = nowUtc,
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
            await docs.CreateAsync(Doc(a, "it_alpha", "A-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(b, "it_beta", "B-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(c, "it_beta", "C-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(d, "it_beta", "D-0001", nowUtc), ct);

            // Cycle: A -> B -> C -> A, plus C -> D.
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
                Id = DeterministicGuid.Create($"DocumentRelationship|{b:D}|{basedOnNorm}|{c:D}"),
                FromDocumentId = b,
                ToDocumentId = c,
                RelationshipCode = basedOn,
                RelationshipCodeNorm = basedOnNorm,
                CreatedAtUtc = nowUtc.AddSeconds(2)
            }, ct);

            await relRepo.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = DeterministicGuid.Create($"DocumentRelationship|{c:D}|{basedOnNorm}|{a:D}"),
                FromDocumentId = c,
                ToDocumentId = a,
                RelationshipCode = basedOn,
                RelationshipCodeNorm = basedOnNorm,
                CreatedAtUtc = nowUtc.AddSeconds(3)
            }, ct);

            await relRepo.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = DeterministicGuid.Create($"DocumentRelationship|{c:D}|{basedOnNorm}|{d:D}"),
                FromDocumentId = c,
                ToDocumentId = d,
                RelationshipCode = basedOn,
                RelationshipCodeNorm = basedOnNorm,
                CreatedAtUtc = nowUtc.AddSeconds(4)
            }, ct);
        }, CancellationToken.None);

        var g = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: a,
            MaxDepth: 2,
            Direction: DocumentRelationshipTraversalDirection.Both,
            RelationshipCodes: null,
            MaxNodes: 100,
            MaxEdges: 100),
            CancellationToken.None);

        g.RootDocumentId.Should().Be(a);
        g.Nodes.Select(n => n.DocumentId).Should().OnlyHaveUniqueItems();
        g.Edges.Select(e => e.RelationshipId).Should().OnlyHaveUniqueItems();

        // Minimal depth: C must be discovered at depth 1 via the incoming edge C -> A
        // (even though it is also reachable as A -> B -> C).
        g.Nodes.Should().ContainSingle(x => x.DocumentId == a && x.Depth == 0);
        g.Nodes.Should().ContainSingle(x => x.DocumentId == b && x.Depth == 1);
        g.Nodes.Should().ContainSingle(x => x.DocumentId == c && x.Depth == 1);
        g.Nodes.Should().ContainSingle(x => x.DocumentId == d && x.Depth == 2);

        // All four relationships should be present exactly once.
        g.Edges.Should().HaveCount(4);
        g.Edges.Should().ContainSingle(x => x.FromDocumentId == a && x.ToDocumentId == b);
        g.Edges.Should().ContainSingle(x => x.FromDocumentId == b && x.ToDocumentId == c);
        g.Edges.Should().ContainSingle(x => x.FromDocumentId == c && x.ToDocumentId == a);
        g.Edges.Should().ContainSingle(x => x.FromDocumentId == c && x.ToDocumentId == d);
    }
}
