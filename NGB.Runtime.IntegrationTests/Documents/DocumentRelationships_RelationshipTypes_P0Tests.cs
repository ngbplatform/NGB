using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_RelationshipTypes_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateAsync_WhenRelationshipTypeUnknown_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var ids = await CreateDraftDocsAsync(scope.ServiceProvider, count: 2);
        var a = ids[0];
        var b = ids[1];
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        Func<Task> act = () => svc.CreateAsync(a, b, relationshipCode: "unknown_code", manageTransaction: true, ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentRelationshipTypeNotFoundException>();
        ex.Which.AssertNgbError(DocumentRelationshipTypeNotFoundException.Code, "relationshipCode");
    }

    [Fact]
    public async Task Cardinality_ManyToOne_EnforcesMaxOutgoingPerFrom()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var ids = await CreateDraftDocsAsync(scope.ServiceProvider, count: 3);
        var from = ids[0];
        var to1 = ids[1];
        var to2 = ids[2];

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        (await svc.CreateAsync(from, to1, relationshipCode: "reversal_of", manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        Func<Task> act = () => svc.CreateAsync(from, to2, relationshipCode: "reversal_of", manageTransaction: true, ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentRelationshipValidationException>();
        ex.Which.AssertNgbError(DocumentRelationshipValidationException.Code, "reason", "relationshipCode", "fromDocumentId", "toDocumentId");
        ex.Which.AssertReason("cardinality_max_outgoing_per_from");
    }

    [Fact]
    public async Task Cardinality_OneToOne_EnforcesMaxIncomingPerTo()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var ids = await CreateDraftDocsAsync(scope.ServiceProvider, count: 3);
        var from1 = ids[0];
        var from2 = ids[1];
        var to = ids[2];

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        (await svc.CreateAsync(from1, to, relationshipCode: "supersedes", manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        Func<Task> act = () => svc.CreateAsync(from2, to, relationshipCode: "supersedes", manageTransaction: true, ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentRelationshipValidationException>();
        ex.Which.AssertNgbError(DocumentRelationshipValidationException.Code, "reason", "relationshipCode", "fromDocumentId", "toDocumentId");
        ex.Which.AssertReason("cardinality_max_incoming_per_to");
    }

    [Fact]
    public async Task BidirectionalType_CreatesAndDeletesBothDirectedEdges()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var ids = await CreateDraftDocsAsync(scope.ServiceProvider, count: 2);
        var a = ids[0];
        var b = ids[1];

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        (await svc.CreateAsync(a, b, relationshipCode: "related_to", manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        var outgoingA = await svc.ListOutgoingAsync(a, CancellationToken.None);
        outgoingA.Should().ContainSingle(x => x.ToDocumentId == b && x.RelationshipCodeNorm == "related_to");

        var outgoingB = await svc.ListOutgoingAsync(b, CancellationToken.None);
        outgoingB.Should().ContainSingle(x => x.ToDocumentId == a && x.RelationshipCodeNorm == "related_to");

        (await svc.DeleteAsync(a, b, relationshipCode: "related_to", manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        (await svc.ListOutgoingAsync(a, CancellationToken.None)).Should().BeEmpty();
        (await svc.ListOutgoingAsync(b, CancellationToken.None)).Should().BeEmpty();
    }

    private static async Task<Guid[]> CreateDraftDocsAsync(IServiceProvider sp, int count)
    {
        if (count <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(count), count, "Count must be positive.");

        var uow = sp.GetRequiredService<IUnitOfWork>();
        var repo = sp.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var ids = Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToArray();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            for (var i = 0; i < ids.Length; i++)
            {
                await repo.CreateAsync(new DocumentRecord
                {
                    Id = ids[i],
                    TypeCode = i == 0 ? "it_alpha" : "it_beta",
                    Number = $"IT-{i + 1:0000}",
                    DateUtc = nowUtc,
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                    PostedAtUtc = null,
                    MarkedForDeletionAtUtc = null
                }, ct);
            }
        }, CancellationToken.None);

        return ids;
    }
}
