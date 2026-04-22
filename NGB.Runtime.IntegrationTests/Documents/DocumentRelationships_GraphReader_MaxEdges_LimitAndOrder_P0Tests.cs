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
public sealed class DocumentRelationships_GraphReader_MaxEdges_LimitAndOrder_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Graph_WhenMaxEdgesIsSmall_RespectsLimit_AndOrdersByCreatedAtThenIdDesc()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var docs = sp.GetRequiredService<IDocumentRepository>();
        var relRepo = sp.GetRequiredService<IDocumentRelationshipRepository>();
        var graph = sp.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var root = Guid.CreateVersion7();
        var child1 = Guid.CreateVersion7();
        var child2 = Guid.CreateVersion7();
        var child3 = Guid.CreateVersion7();
        var child4 = Guid.CreateVersion7();

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

        // Make two edges share the same CreatedAtUtc to exercise tie-breaking by RelationshipId DESC.
        var e1 = new DocumentRelationshipRecord
        {
            Id = DeterministicGuid.Create($"DocumentRelationship|{root:D}|{basedOnNorm}|{child1:D}"),
            FromDocumentId = root,
            ToDocumentId = child1,
            RelationshipCode = basedOn,
            RelationshipCodeNorm = basedOnNorm,
            CreatedAtUtc = nowUtc.AddSeconds(10)
        };

        var e2 = new DocumentRelationshipRecord
        {
            Id = DeterministicGuid.Create($"DocumentRelationship|{root:D}|{basedOnNorm}|{child2:D}"),
            FromDocumentId = root,
            ToDocumentId = child2,
            RelationshipCode = basedOn,
            RelationshipCodeNorm = basedOnNorm,
            CreatedAtUtc = nowUtc.AddSeconds(9)
        };

        var e3 = new DocumentRelationshipRecord
        {
            Id = DeterministicGuid.Create($"DocumentRelationship|{root:D}|{basedOnNorm}|{child3:D}"),
            FromDocumentId = root,
            ToDocumentId = child3,
            RelationshipCode = basedOn,
            RelationshipCodeNorm = basedOnNorm,
            CreatedAtUtc = nowUtc.AddSeconds(9)
        };

        var e4 = new DocumentRelationshipRecord
        {
            Id = DeterministicGuid.Create($"DocumentRelationship|{root:D}|{basedOnNorm}|{child4:D}"),
            FromDocumentId = root,
            ToDocumentId = child4,
            RelationshipCode = basedOn,
            RelationshipCodeNorm = basedOnNorm,
            CreatedAtUtc = nowUtc.AddSeconds(8)
        };

        var all = new[] { e1, e2, e3, e4 };
        var expected = all
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(3)
            .Select(x => x.Id)
            .ToArray();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(Doc(root, "it_root", "R-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(child1, "it_child", "C-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(child2, "it_child", "C-0002", nowUtc), ct);
            await docs.CreateAsync(Doc(child3, "it_child", "C-0003", nowUtc), ct);
            await docs.CreateAsync(Doc(child4, "it_child", "C-0004", nowUtc), ct);

            foreach (var rel in all)
                await relRepo.TryCreateAsync(rel, ct);
        }, CancellationToken.None);

        var g = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: root,
            MaxDepth: 1,
            Direction: DocumentRelationshipTraversalDirection.Outgoing,
            RelationshipCodes: null,
            MaxNodes: 100,
            MaxEdges: 3),
            CancellationToken.None);

        g.Edges.Should().HaveCount(3);

        // Contract: Graph edges are ordered by CreatedAtUtc DESC, RelationshipId DESC.
        g.Edges.Select(x => x.RelationshipId).Should().Equal(expected);
    }
}
