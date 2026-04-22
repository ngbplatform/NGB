using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_GraphReader_RequestValidation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Graph_WhenRootDocumentIdIsEmpty_ThrowsArgumentRequiredException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var graph = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        Func<Task> act = () => graph.GetGraphAsync(
            new DocumentRelationshipGraphRequest(
                RootDocumentId: Guid.Empty,
                MaxDepth: 1,
                Direction: DocumentRelationshipTraversalDirection.Both,
                RelationshipCodes: null,
                MaxNodes: 100,
                MaxEdges: 100),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("RootDocumentId");
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task Graph_WhenMaxDepthIsOutOfRange_ThrowsArgumentOutOfRangeException(int maxDepth)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var graph = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        Func<Task> act = () => graph.GetGraphAsync(
            new DocumentRelationshipGraphRequest(
                RootDocumentId: Guid.CreateVersion7(),
                MaxDepth: maxDepth,
                Direction: DocumentRelationshipTraversalDirection.Both,
                RelationshipCodes: null,
                MaxNodes: 100,
                MaxEdges: 100),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("MaxDepth");
        ex.Which.AssertNgbError(NgbArgumentOutOfRangeException.Code, "paramName", "actualValue", "reason");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Graph_WhenMaxNodesIsNonPositive_ThrowsArgumentOutOfRangeException(int maxNodes)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var graph = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        Func<Task> act = () => graph.GetGraphAsync(
            new DocumentRelationshipGraphRequest(
                RootDocumentId: Guid.CreateVersion7(),
                MaxDepth: 1,
                Direction: DocumentRelationshipTraversalDirection.Both,
                RelationshipCodes: null,
                MaxNodes: maxNodes,
                MaxEdges: 100),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("MaxNodes");
        ex.Which.AssertNgbError(NgbArgumentOutOfRangeException.Code, "paramName", "actualValue", "reason");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Graph_WhenMaxEdgesIsNonPositive_ThrowsArgumentOutOfRangeException(int maxEdges)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var graph = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        Func<Task> act = () => graph.GetGraphAsync(
            new DocumentRelationshipGraphRequest(
                RootDocumentId: Guid.CreateVersion7(),
                MaxDepth: 1,
                Direction: DocumentRelationshipTraversalDirection.Both,
                RelationshipCodes: null,
                MaxNodes: 100,
                MaxEdges: maxEdges),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("MaxEdges");
        ex.Which.AssertNgbError(NgbArgumentOutOfRangeException.Code, "paramName", "actualValue", "reason");
    }

    [Fact]
    public async Task Graph_WhenRootDocumentDoesNotExist_ThrowsDocumentNotFound()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var graph = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var missingId = Guid.CreateVersion7();

        Func<Task> act = () => graph.GetGraphAsync(
            new DocumentRelationshipGraphRequest(
                RootDocumentId: missingId,
                MaxDepth: 1,
                Direction: DocumentRelationshipTraversalDirection.Both,
                RelationshipCodes: null,
                MaxNodes: 100,
                MaxEdges: 100),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentNotFoundException>();
        ex.Which.AssertNgbError(DocumentNotFoundException.Code, "documentId");
    }

    [Fact]
    public async Task Graph_WhenMaxDepthIsZero_ReturnsOnlyRootNode_AndNoEdges()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var docs = sp.GetRequiredService<IDocumentRepository>();
        var graph = sp.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var rootId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = rootId,
                TypeCode = "it_root",
                Number = "R-0001",
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        var g = await graph.GetGraphAsync(new DocumentRelationshipGraphRequest(
            RootDocumentId: rootId,
            MaxDepth: 0,
            Direction: DocumentRelationshipTraversalDirection.Both,
            RelationshipCodes: null,
            MaxNodes: 100,
            MaxEdges: 100), CancellationToken.None);

        g.RootDocumentId.Should().Be(rootId);
        g.Nodes.Should().ContainSingle(n => n.DocumentId == rootId && n.Depth == 0);
        g.Edges.Should().BeEmpty();
    }
}
