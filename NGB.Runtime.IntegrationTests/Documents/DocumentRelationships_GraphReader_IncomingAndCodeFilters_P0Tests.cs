using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_GraphReader_IncomingAndCodeFilters_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task IncomingPage_KeysetPaging_IsStable_AndReturnsAllRows()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var docs = sp.GetRequiredService<IDocumentRepository>();
        var relRepo = sp.GetRequiredService<IDocumentRelationshipRepository>();
        var graph = sp.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var toId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

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

        var sources = Enumerable.Range(1, 5)
            .Select(i => Doc(Guid.CreateVersion7(), "it_src", $"S-{i:0000}", nowUtc))
            .ToList();

        const string code = "based_on";
        var codeNorm = code.ToLowerInvariant();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(Doc(toId, "it_target", "T-0001", nowUtc), ct);
            foreach (var s in sources)
                await docs.CreateAsync(s, ct);

            for (var i = 0; i < sources.Count; i++)
            {
                var fromId = sources[i].Id;
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
        var expected = sources
            .Select((s, i) => new
            {
                FromId = s.Id,
                CreatedAtUtc = nowUtc.AddSeconds(i + 1),
                RelId = DeterministicGuid.Create($"DocumentRelationship|{s.Id:D}|{codeNorm}|{toId:D}")
            })
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.RelId)
            .ToList();

        var all = new List<Guid>();
        DocumentRelationshipEdgeCursor? cursor = null;

        var p1 = await graph.GetIncomingPageAsync(new DocumentRelationshipEdgePageRequest(toId, RelationshipCode: code, PageSize: 2, Cursor: cursor), CancellationToken.None);
        p1.Items.Should().HaveCount(2);
        p1.HasMore.Should().BeTrue();
        p1.NextCursor.Should().NotBeNull();
        all.AddRange(p1.Items.Select(x => x.RelationshipId));
        cursor = p1.NextCursor;

        var p2 = await graph.GetIncomingPageAsync(new DocumentRelationshipEdgePageRequest(toId, RelationshipCode: code, PageSize: 2, Cursor: cursor), CancellationToken.None);
        p2.Items.Should().HaveCount(2);
        p2.HasMore.Should().BeTrue();
        p2.NextCursor.Should().NotBeNull();
        all.AddRange(p2.Items.Select(x => x.RelationshipId));
        cursor = p2.NextCursor;

        var p3 = await graph.GetIncomingPageAsync(new DocumentRelationshipEdgePageRequest(toId, RelationshipCode: code, PageSize: 2, Cursor: cursor), CancellationToken.None);
        p3.Items.Should().HaveCount(1);
        p3.HasMore.Should().BeFalse();
        p3.NextCursor.Should().BeNull();
        all.AddRange(p3.Items.Select(x => x.RelationshipId));

        all.Should().Equal(expected);
        p1.Items.All(i => i.OtherDocument.TypeCode == "it_src").Should().BeTrue();
    }

    [Fact]
    public async Task OutgoingPage_RelationshipCode_IsTrimmedAndLowercased_AndBlankMeansAllCodes()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var docs = sp.GetRequiredService<IDocumentRepository>();
        var relRepo = sp.GetRequiredService<IDocumentRelationshipRepository>();
        var graph = sp.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var fromId = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();

        var nowUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

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
        const string createdFrom = "created_from";

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(Doc(fromId, "it_alpha", "A-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(b, "it_beta", "B-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(c, "it_beta", "C-0001", nowUtc), ct);

            await relRepo.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{basedOn}|{b:D}"),
                FromDocumentId = fromId,
                ToDocumentId = b,
                RelationshipCode = basedOn,
                RelationshipCodeNorm = basedOn,
                CreatedAtUtc = nowUtc.AddSeconds(1)
            }, ct);

            await relRepo.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{createdFrom}|{c:D}"),
                FromDocumentId = fromId,
                ToDocumentId = c,
                RelationshipCode = createdFrom,
                RelationshipCodeNorm = createdFrom,
                CreatedAtUtc = nowUtc.AddSeconds(2)
            }, ct);
        }, CancellationToken.None);

        // Filter by code but pass dirty casing/whitespace.
        var filtered = await graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: "  BASED_ON  ", PageSize: 100, Cursor: null),
            CancellationToken.None);

        filtered.Items.Should().HaveCount(1);
        filtered.Items[0].RelationshipCodeNorm.Should().Be("based_on");
        filtered.Items[0].OtherDocument.DocumentId.Should().Be(b);

        // Blank means "all codes".
        var allCodes = await graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: "   ", PageSize: 100, Cursor: null),
            CancellationToken.None);

        allCodes.Items.Should().HaveCount(2);
        allCodes.Items.Select(x => x.RelationshipCodeNorm).Should().BeEquivalentTo(new[] { "based_on", "created_from" });
    }

    [Fact]
    public async Task OutgoingPage_WhenCreatedAtTies_UsesRelationshipIdAsTieBreaker_ForKeysetPaging()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var docs = sp.GetRequiredService<IDocumentRepository>();
        var relRepo = sp.GetRequiredService<IDocumentRelationshipRepository>();
        var graph = sp.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var fromId = Guid.CreateVersion7();
        var to1 = Guid.CreateVersion7();
        var to2 = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

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

        const string code = "based_on";
        var codeNorm = code.ToLowerInvariant();
        var sameCreatedAt = nowUtc.AddSeconds(5);

        var relId1 = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{to1:D}");
        var relId2 = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{to2:D}");

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(Doc(fromId, "it_alpha", "A-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(to1, "it_beta", "B-0001", nowUtc), ct);
            await docs.CreateAsync(Doc(to2, "it_beta", "B-0002", nowUtc), ct);

            await relRepo.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = relId1,
                FromDocumentId = fromId,
                ToDocumentId = to1,
                RelationshipCode = code,
                RelationshipCodeNorm = codeNorm,
                CreatedAtUtc = sameCreatedAt
            }, ct);

            await relRepo.TryCreateAsync(new DocumentRelationshipRecord
            {
                Id = relId2,
                FromDocumentId = fromId,
                ToDocumentId = to2,
                RelationshipCode = code,
                RelationshipCodeNorm = codeNorm,
                CreatedAtUtc = sameCreatedAt
            }, ct);
        }, CancellationToken.None);

        // Order is created_at desc then relationship_id desc.
        var expected = new[] { relId1, relId2 }.OrderByDescending(x => x).ToList();

        var p = await graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 2, Cursor: null),
            CancellationToken.None);

        p.Items.Select(x => x.RelationshipId).Should().Equal(expected);

        // Now verify cursor respects the tie-breaker: page size 1 then page size 1.
        var p1 = await graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 1, Cursor: null),
            CancellationToken.None);

        p1.Items.Should().HaveCount(1);
        p1.HasMore.Should().BeTrue();
        p1.NextCursor.Should().NotBeNull();

        var p2 = await graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(fromId, RelationshipCode: code, PageSize: 1, Cursor: p1.NextCursor),
            CancellationToken.None);

        p2.Items.Should().HaveCount(1);
        p2.HasMore.Should().BeFalse();
        p2.NextCursor.Should().BeNull();

        new[] { p1.Items[0].RelationshipId, p2.Items[0].RelationshipId }.Should().Equal(expected);
    }

    [Fact]
    public async Task OutgoingPage_WhenRelationshipCodeExceedsMaxLength_ThrowsArgumentInvalidException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var graph = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var tooLong = new string('a', 129);

        Func<Task> act = () => graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(Guid.CreateVersion7(), RelationshipCode: tooLong, PageSize: 100, Cursor: null),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("RelationshipCode");
        ex.Which.AssertNgbError(NgbArgumentInvalidException.Code, "paramName", "reason");
    }
}
