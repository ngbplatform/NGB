using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_GraphReader_CodeLengthValidation_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OutgoingPage_WhenRelationshipCodeTooLong_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var graph = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();
        var tooLong = new string('a', 129);

        Func<Task> act = () => graph.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(
                DocumentId: Guid.CreateVersion7(),
                RelationshipCode: tooLong,
                PageSize: 10,
                Cursor: null),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.Message.Should().Contain("max length 128");
        ex.Which.ParamName.Should().Be("RelationshipCode");
        ex.Which.AssertNgbError(NgbArgumentInvalidException.Code, "paramName", "reason");
    }

    [Fact]
    public async Task IncomingPage_WhenRelationshipCodeTooLong_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var graph = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();
        var tooLong = new string('a', 129);

        Func<Task> act = () => graph.GetIncomingPageAsync(
            new DocumentRelationshipEdgePageRequest(
                DocumentId: Guid.CreateVersion7(),
                RelationshipCode: tooLong,
                PageSize: 10,
                Cursor: null),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.Message.Should().Contain("max length 128");
        ex.Which.ParamName.Should().Be("RelationshipCode");
        ex.Which.AssertNgbError(NgbArgumentInvalidException.Code, "paramName", "reason");
    }

    [Fact]
    public async Task Graph_WhenAnyRelationshipCodeTooLong_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var graph = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();
        var tooLong = new string('a', 129);

        Func<Task> act = () => graph.GetGraphAsync(
            new DocumentRelationshipGraphRequest(
                RootDocumentId: Guid.CreateVersion7(),
                MaxDepth: 1,
                Direction: DocumentRelationshipTraversalDirection.Both,
                RelationshipCodes: new[] { tooLong },
                MaxNodes: 50,
                MaxEdges: 50),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.Message.Should().Contain("max length 128");
        ex.Which.ParamName.Should().Be("RelationshipCodes");
        ex.Which.AssertNgbError(NgbArgumentInvalidException.Code, "paramName", "reason");
    }
}
