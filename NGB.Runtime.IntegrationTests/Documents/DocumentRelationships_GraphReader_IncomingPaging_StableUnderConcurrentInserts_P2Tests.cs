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
public sealed class DocumentRelationships_GraphReader_IncomingPaging_StableUnderConcurrentInserts_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task IncomingPage_KeysetPaging_DoesNotJump_WhenNewerRowsInsertedBetweenPages()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var docs = sp.GetRequiredService<IDocumentRepository>();
        var relRepo = sp.GetRequiredService<IDocumentRelationshipRepository>();
        var graph = sp.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var rootId = Guid.CreateVersion7(); // this is the "to" document for incoming edges
        var nowUtc = new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc);

        const string code = "based_on";
        var codeNorm = code.ToLowerInvariant();

        // Arrange: create root + 5 source docs, and 5 incoming edges (src -> root).
        var initialSources = Enumerable.Range(1, 5)
            .Select(i => new DocumentRecord
            {
                Id = Guid.CreateVersion7(),
                TypeCode = "it_src",
                Number = $"SRC-{i:0000}",
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            })
            .ToList();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = rootId,
                TypeCode = "it_root",
                Number = "ROOT-0001",
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            foreach (var s in initialSources)
                await docs.CreateAsync(s, ct);

            for (var i = 0; i < initialSources.Count; i++)
            {
                var fromId = initialSources[i].Id;
                var createdAt = nowUtc.AddSeconds(i + 1); // 1..5
                var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{rootId:D}");

                await relRepo.TryCreateAsync(new DocumentRelationshipRecord
                {
                    Id = relId,
                    FromDocumentId = fromId,
                    ToDocumentId = rootId,
                    RelationshipCode = code,
                    RelationshipCodeNorm = codeNorm,
                    CreatedAtUtc = createdAt
                }, ct);
            }
        }, CancellationToken.None);

        var initialExpected = initialSources
            .Select((s, i) => new
            {
                RelId = DeterministicGuid.Create($"DocumentRelationship|{s.Id:D}|{codeNorm}|{rootId:D}"),
                CreatedAtUtc = nowUtc.AddSeconds(i + 1)
            })
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.RelId)
            .ToList();

        // Page 1
        DocumentRelationshipEdgeCursor? cursor = null;
        var p1 = await graph.GetIncomingPageAsync(
            new DocumentRelationshipEdgePageRequest(rootId, RelationshipCode: code, PageSize: 2, Cursor: cursor),
            CancellationToken.None);

        p1.Items.Should().HaveCount(2);
        p1.HasMore.Should().BeTrue();
        p1.NextCursor.Should().NotBeNull();
        cursor = p1.NextCursor;

        // Insert NEWER rows between pages (new sources -> root).
        var insertedSources = Enumerable.Range(1, 3)
            .Select(i => new DocumentRecord
            {
                Id = Guid.CreateVersion7(),
                TypeCode = "it_src",
                Number = $"SRC-NEW-{i:0000}",
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            })
            .ToList();

        var insertBase = nowUtc.AddMinutes(10); // definitely newer than any initial edge

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            foreach (var s in insertedSources)
                await docs.CreateAsync(s, ct);

            for (var i = 0; i < insertedSources.Count; i++)
            {
                var fromId = insertedSources[i].Id;
                var createdAt = insertBase.AddSeconds(i + 1); // 10:00:01..10:00:03
                var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{rootId:D}");

                await relRepo.TryCreateAsync(new DocumentRelationshipRecord
                {
                    Id = relId,
                    FromDocumentId = fromId,
                    ToDocumentId = rootId,
                    RelationshipCode = code,
                    RelationshipCodeNorm = codeNorm,
                    CreatedAtUtc = createdAt
                }, ct);
            }
        }, CancellationToken.None);

        var insertedExpectedTop = insertedSources
            .Select((s, i) => new
            {
                RelId = DeterministicGuid.Create($"DocumentRelationship|{s.Id:D}|{codeNorm}|{rootId:D}"),
                CreatedAtUtc = insertBase.AddSeconds(i + 1)
            })
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.RelId)
            .ToList();

        // Continue paging from the old cursor: MUST return only remaining initial edges.
        var allPaged = new List<Guid>();
        allPaged.AddRange(p1.Items.Select(x => x.RelationshipId));

        var p2 = await graph.GetIncomingPageAsync(
            new DocumentRelationshipEdgePageRequest(rootId, RelationshipCode: code, PageSize: 2, Cursor: cursor),
            CancellationToken.None);
        allPaged.AddRange(p2.Items.Select(x => x.RelationshipId));

        var p3 = await graph.GetIncomingPageAsync(
            new DocumentRelationshipEdgePageRequest(rootId, RelationshipCode: code, PageSize: 10, Cursor: p2.NextCursor),
            CancellationToken.None);
        allPaged.AddRange(p3.Items.Select(x => x.RelationshipId));

        allPaged.Should().Equal(initialExpected, "cursor paging must be stable even if newer rows are inserted between pages");
        allPaged.Should().NotContain(insertedExpectedTop);

        // Sanity: a fresh first page SHOULD show the newly inserted edges at the top.
        var fresh = await graph.GetIncomingPageAsync(
            new DocumentRelationshipEdgePageRequest(rootId, RelationshipCode: code, PageSize: 3, Cursor: null),
            CancellationToken.None);

        fresh.Items.Should().HaveCount(3);
        fresh.Items.Select(i => i.RelationshipId).Should().Equal(insertedExpectedTop);
    }
}
