using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_GraphReader_RequestValidation_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OutgoingPage_WhenRelationshipCodeTooLong_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var tooLong = new string('a', 129);

        var act = () => svc.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(
                DocumentId: Guid.CreateVersion7(),
                RelationshipCode: tooLong,
                Cursor: null,
                PageSize: 10),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.Message.Should().Contain("max length 128");
        ex.Which.ParamName.Should().Be("RelationshipCode");
        ex.Which.AssertNgbError(NgbArgumentInvalidException.Code, "paramName", "reason");
    }

    [Fact]
    public async Task IncomingPage_WhenRelationshipCodeWhitespace_TreatedAsNullFilter_DoesNotThrow()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var page = await svc.GetIncomingPageAsync(
            new DocumentRelationshipEdgePageRequest(
                DocumentId: Guid.CreateVersion7(),
                RelationshipCode: "   \t  ",
                Cursor: null,
                PageSize: 10),
            CancellationToken.None);

        page.Items.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Graph_WhenAnyRelationshipCodeTooLong_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipGraphReadService>();

        var ok = "based_on";
        var tooLong = new string('b', 129);

        var act = () => svc.GetGraphAsync(
            new DocumentRelationshipGraphRequest(
                RootDocumentId: Guid.CreateVersion7(),
                RelationshipCodes: new[] { ok, tooLong },
                Direction: DocumentRelationshipTraversalDirection.Outgoing,
                MaxDepth: 1,
                MaxNodes: 10,
                MaxEdges: 10),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.Message.Should().Contain("max length 128");
        ex.Which.ParamName.Should().Be("RelationshipCodes");
        ex.Which.AssertNgbError(NgbArgumentInvalidException.Code, "paramName", "reason");
    }
}
