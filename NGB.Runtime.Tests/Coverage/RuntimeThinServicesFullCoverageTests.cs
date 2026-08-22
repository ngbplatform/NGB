using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Persistence.Readers.Documents;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Documents;
using NGB.Runtime.Posting;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Coverage;

public sealed class RuntimeThinServicesFullCoverageTests
{
    [Fact]
    public async Task DocumentRelationshipGraphReadService_NormalizesAllPageBoundariesAndDelegatesGraph()
    {
        var documentId = Guid.CreateVersion7();
        var page = new DocumentRelationshipEdgePage([], false, null);
        var graphRequest = new DocumentRelationshipGraphRequest(documentId);
        var graph = new DocumentRelationshipGraph(documentId, [], []);
        using var cancellation = new CancellationTokenSource();
        var reader = new Mock<IDocumentRelationshipGraphReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetOutgoingPageAsync(
                It.Is<DocumentRelationshipEdgePageRequest>(request => request.PageSize == 100),
                cancellation.Token))
            .ReturnsAsync(page);
        reader.Setup(x => x.GetIncomingPageAsync(
                It.Is<DocumentRelationshipEdgePageRequest>(request => request.PageSize == 500),
                cancellation.Token))
            .ReturnsAsync(page);
        var unchanged = new DocumentRelationshipEdgePageRequest(documentId, PageSize: 42);
        reader.Setup(x => x.GetOutgoingPageAsync(unchanged, cancellation.Token))
            .ReturnsAsync(page);
        reader.Setup(x => x.GetGraphAsync(graphRequest, cancellation.Token))
            .ReturnsAsync(graph);
        var service = new DocumentRelationshipGraphReadService(reader.Object);

        (await service.GetOutgoingPageAsync(
            new DocumentRelationshipEdgePageRequest(documentId, PageSize: 0), cancellation.Token)).Should().BeSameAs(page);
        (await service.GetIncomingPageAsync(
            new DocumentRelationshipEdgePageRequest(documentId, PageSize: 501), cancellation.Token)).Should().BeSameAs(page);
        (await service.GetOutgoingPageAsync(unchanged, cancellation.Token)).Should().BeSameAs(page);
        (await service.GetGraphAsync(graphRequest, cancellation.Token)).Should().BeSameAs(graph);

        reader.VerifyAll();
    }

    [Fact]
    public async Task AccountingPostingContextFactory_UsesProviderChartAndCancellationToken()
    {
        var chart = new ChartOfAccounts();
        using var cancellation = new CancellationTokenSource();
        var provider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        provider.Setup(x => x.GetAsync(cancellation.Token)).ReturnsAsync(chart);

        var context = await new AccountingPostingContextFactory(provider.Object)
            .CreateAsync(cancellation.Token);

        (await context.GetChartOfAccountsAsync()).Should().BeSameAs(chart);
        provider.VerifyAll();
    }

    [Fact]
    public async Task PostingStateReportService_ValidRequest_AppliesDefaultAndDelegatesExactRequest()
    {
        var request = new PostingStatePageRequest
        {
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };
        var page = new PostingStatePage([], false, null);
        using var cancellation = new CancellationTokenSource();
        var reader = new Mock<IPostingStateReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetPageAsync(request, cancellation.Token)).ReturnsAsync(page);

        var result = await new PostingStateReportService(reader.Object)
            .GetPageAsync(request, cancellation.Token);

        result.Should().BeSameAs(page);
        request.StaleAfter.Should().Be(TimeSpan.FromMinutes(10));
        reader.VerifyAll();
    }

    [Fact]
    public async Task PostingStateReportService_NonUtcBound_RejectsBeforeReaderCall()
    {
        var request = new PostingStatePageRequest
        {
            FromUtc = DateTime.SpecifyKind(new DateTime(2026, 1, 1), DateTimeKind.Unspecified),
            ToUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };
        var reader = new Mock<IPostingStateReader>(MockBehavior.Strict);

        var action = () => new PostingStateReportService(reader.Object).GetPageAsync(request);

        await action.Should().ThrowAsync<NgbArgumentInvalidException>();
        reader.VerifyNoOtherCalls();
    }
}
