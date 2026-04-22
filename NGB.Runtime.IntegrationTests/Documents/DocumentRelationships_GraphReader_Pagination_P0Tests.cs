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
public sealed class DocumentRelationships_GraphReader_Pagination_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OutgoingPage_KeysetPaging_IsStable_AndReturnsAllRows()
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

        var targets = Enumerable.Range(1, 5).Select(i => new DocumentRecord
        {
            Id = Guid.CreateVersion7(),
            TypeCode = "it_beta",
            Number = $"B-{i:0000}",
            DateUtc = new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        }).ToList();

        // Insert documents + relationships with controlled CreatedAtUtc to make ordering deterministic.
        const string code = "based_on";
        var codeNorm = code.ToLowerInvariant();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            foreach (var t in targets)
                await docs.CreateAsync(t, ct);

            for (var i = 0; i < targets.Count; i++)
            {
                var toId = targets[i].Id;
                var createdAt = nowUtc.AddSeconds(i + 1); // strictly increasing
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

        // Expected order: CreatedAtUtc DESC.
        var expected = targets
            .Select((t, i) => new
            {
                ToId = t.Id,
                CreatedAtUtc = nowUtc.AddSeconds(i + 1),
                RelId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{t.Id:D}")
            })
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.RelId)
            .ToList();

        var all = new List<Guid>();
        DocumentRelationshipEdgeCursor? cursor = null;

        // Page 1
        var p1 = await graph.GetOutgoingPageAsync(new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 2, Cursor: cursor), CancellationToken.None);
        p1.Items.Should().HaveCount(2);
        p1.HasMore.Should().BeTrue();
        p1.NextCursor.Should().NotBeNull();
        all.AddRange(p1.Items.Select(x => x.RelationshipId));
        cursor = p1.NextCursor;

        // Page 2
        var p2 = await graph.GetOutgoingPageAsync(new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 2, Cursor: cursor), CancellationToken.None);
        p2.Items.Should().HaveCount(2);
        p2.HasMore.Should().BeTrue();
        p2.NextCursor.Should().NotBeNull();
        all.AddRange(p2.Items.Select(x => x.RelationshipId));
        cursor = p2.NextCursor;

        // Page 3
        var p3 = await graph.GetOutgoingPageAsync(new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 2, Cursor: cursor), CancellationToken.None);
        p3.Items.Should().HaveCount(1);
        p3.HasMore.Should().BeFalse();
        p3.NextCursor.Should().BeNull();
        all.AddRange(p3.Items.Select(x => x.RelationshipId));

        all.Should().Equal(expected);
        p1.Items.All(i => i.OtherDocument.TypeCode == "it_beta").Should().BeTrue();
    }

    [Fact]
    public async Task OutgoingPage_PageSize_IsNormalized_ToDefault100_WhenNonPositive()
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

        // Arrange: 150 outgoing edges so default(100) is observable.
        const int edgeCount = 150;
        const string code = "based_on";
        var codeNorm = code.ToLowerInvariant();

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

            for (var i = 0; i < edgeCount; i++)
            {
                var toId = Guid.CreateVersion7();
                await docs.CreateAsync(new DocumentRecord
                {
                    Id = toId,
                    TypeCode = "it_beta",
                    Number = $"B-{i + 1:0000}",
                    DateUtc = nowUtc,
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                    PostedAtUtc = null,
                    MarkedForDeletionAtUtc = null
                }, ct);

                await relRepo.TryCreateAsync(new DocumentRelationshipRecord
                {
                    Id = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{toId:D}"),
                    FromDocumentId = fromId,
                    ToDocumentId = toId,
                    RelationshipCode = code,
                    RelationshipCodeNorm = codeNorm,
                    CreatedAtUtc = nowUtc.AddSeconds(i + 1)
                }, ct);
            }
        }, CancellationToken.None);

        // Act: PageSize <= 0 should normalize to default 100.
        var p = await graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 0, Cursor: null),
            CancellationToken.None);

        // Assert
        p.Items.Should().HaveCount(100);
        p.HasMore.Should().BeTrue();
        p.NextCursor.Should().NotBeNull();
    }

    [Fact]
    public async Task OutgoingPage_PageSize_IsCapped_At500_WhenTooLarge()
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

        // Arrange: 550 outgoing edges so max(500) is observable.
        const int edgeCount = 550;
        const string code = "based_on";
        var codeNorm = code.ToLowerInvariant();

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

            for (var i = 0; i < edgeCount; i++)
            {
                var toId = Guid.CreateVersion7();
                await docs.CreateAsync(new DocumentRecord
                {
                    Id = toId,
                    TypeCode = "it_beta",
                    Number = $"B-{i + 1:0000}",
                    DateUtc = nowUtc,
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                    PostedAtUtc = null,
                    MarkedForDeletionAtUtc = null
                }, ct);

                await relRepo.TryCreateAsync(new DocumentRelationshipRecord
                {
                    Id = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{toId:D}"),
                    FromDocumentId = fromId,
                    ToDocumentId = toId,
                    RelationshipCode = code,
                    RelationshipCodeNorm = codeNorm,
                    CreatedAtUtc = nowUtc.AddSeconds(i + 1)
                }, ct);
            }
        }, CancellationToken.None);

        // Act: PageSize > 500 should cap to 500.
        var p = await graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 100_000, Cursor: null),
            CancellationToken.None);

        // Assert
        p.Items.Should().HaveCount(500);
        p.HasMore.Should().BeTrue();
        p.NextCursor.Should().NotBeNull();
    }
}
