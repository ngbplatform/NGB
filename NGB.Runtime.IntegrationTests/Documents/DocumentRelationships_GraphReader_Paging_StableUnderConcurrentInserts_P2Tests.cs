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
public sealed class DocumentRelationships_GraphReader_Paging_StableUnderConcurrentInserts_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OutgoingPage_KeysetPaging_DoesNotJump_WhenNewerRowsInsertedBetweenPages()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var docs = sp.GetRequiredService<IDocumentRepository>();
        var relRepo = sp.GetRequiredService<IDocumentRelationshipRepository>();
        var graph = sp.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var fromId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc);

        const string code = "based_on";
        var codeNorm = code.ToLowerInvariant();

        var initialTargets = Enumerable.Range(1, 5)
            .Select(i => new DocumentRecord
            {
                Id = Guid.CreateVersion7(),
                TypeCode = "it_beta",
                Number = $"B-{i:0000}",
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            })
            .ToList();

        // Arrange: initial 5 edges with strictly increasing CreatedAtUtc (order will be DESC in reads).
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            foreach (var t in initialTargets)
                await docs.CreateAsync(t, ct);

            for (var i = 0; i < initialTargets.Count; i++)
            {
                var toId = initialTargets[i].Id;
                var createdAt = nowUtc.AddSeconds(i + 1); // 1..5
                var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{toId:D}");

                await relRepo.TryCreateAsync(new DocumentRelationshipRecord
                {
                    Id = relId,
                    FromDocumentId = fromId,
                    ToDocumentId = toId,
                    RelationshipCode = code,
                    RelationshipCodeNorm = codeNorm,
                    CreatedAtUtc = createdAt
                }, ct);
            }
        }, CancellationToken.None);

        var initialExpected = initialTargets
            .Select((t, i) => new
            {
                RelId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{t.Id:D}"),
                CreatedAtUtc = nowUtc.AddSeconds(i + 1)
            })
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.RelId)
            .ToList();

        // Page 1 (captures a cursor that must remain stable, even if newer rows are inserted later).
        var p1 = await graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 2, Cursor: null),
            CancellationToken.None);

        p1.Items.Should().HaveCount(2);
        p1.HasMore.Should().BeTrue();
        p1.NextCursor.Should().NotBeNull();

        // Insert NEWER rows between pages (these must NOT appear when continuing from the old cursor).
        var insertedTargets = Enumerable.Range(1, 3)
            .Select(i => new DocumentRecord
            {
                Id = Guid.CreateVersion7(),
                TypeCode = "it_beta",
                Number = $"B-NEW-{i:0000}",
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            })
            .ToList();

        var insertBase = nowUtc.AddMinutes(10); // definitely newer than any initial edge (+1..+5 seconds)

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            foreach (var t in insertedTargets)
                await docs.CreateAsync(t, ct);

            for (var i = 0; i < insertedTargets.Count; i++)
            {
                var toId = insertedTargets[i].Id;
                var createdAt = insertBase.AddSeconds(i + 1);
                var relId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{toId:D}");

                await relRepo.TryCreateAsync(new DocumentRelationshipRecord
                {
                    Id = relId,
                    FromDocumentId = fromId,
                    ToDocumentId = toId,
                    RelationshipCode = code,
                    RelationshipCodeNorm = codeNorm,
                    CreatedAtUtc = createdAt
                }, ct);
            }
        }, CancellationToken.None);

        var insertedExpectedTop = insertedTargets
            .Select((t, i) => new
            {
                RelId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{t.Id:D}"),
                CreatedAtUtc = insertBase.AddSeconds(i + 1)
            })
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.RelId)
            .ToList();

        // Continue paging from old cursor: MUST return only remaining initial edges.
        var allPaged = new List<Guid>();
        allPaged.AddRange(p1.Items.Select(x => x.RelationshipId));

        var p2 = await graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 2, Cursor: p1.NextCursor),
            CancellationToken.None);
        allPaged.AddRange(p2.Items.Select(x => x.RelationshipId));

        var p3 = await graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 10, Cursor: p2.NextCursor),
            CancellationToken.None);
        allPaged.AddRange(p3.Items.Select(x => x.RelationshipId));

        allPaged.Should().Equal(initialExpected, "cursor paging must be stable even if newer rows are inserted between pages");
        allPaged.Intersect(insertedExpectedTop).Should().BeEmpty("newer rows inserted later must not appear when continuing from an older cursor");

        // Sanity: a fresh first page SHOULD show the newly inserted edges at the top.
        var fresh = await graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 3, Cursor: null),
            CancellationToken.None);

        fresh.Items.Should().HaveCount(3);
        fresh.Items.Select(i => i.RelationshipId).Should().Equal(insertedExpectedTop);
    }
}
