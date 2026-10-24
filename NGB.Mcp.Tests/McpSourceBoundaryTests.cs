using Microsoft.Extensions.Options;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Mcp.Tools;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpSourceBoundaryTests
{
    [Theory]
    [InlineData(McpSourceKind.Catalog)]
    [InlineData(McpSourceKind.Document)]
    [InlineData(McpSourceKind.GeneralJournalEntry)]
    [InlineData(McpSourceKind.Report)]
    public async Task MissingRecordPagesDoNotInventNavigationSources(McpSourceKind kind)
    {
        var tool = new ListRecordsTool(Mock.Of<IAuthorizedCatalogReadService>(),
            Mock.Of<IAuthorizedDocumentReadService>(), Mock.Of<IAuthorizedGeneralJournalEntryReadService>());

        var sources = await tool.GetSourcesAsync(new(kind, "sample.record"), new(), default);

        Assert.Empty(sources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRecordOrDisplayCannotBecomeAVerifiedSource(bool hasSource)
    {
        var tool = new GetRecordTool(Mock.Of<IAuthorizedCatalogReadService>(), Mock.Of<IAuthorizedDocumentReadService>(),
            Mock.Of<IAuthorizedGeneralJournalEntryReadService>(), Mock.Of<IRecordReadScopeFactory>(),
            Options.Create(new McpReadOptions()), Mock.Of<IMcpNgbLinkResolver>());
        var id = Guid.NewGuid();
        var result = new RecordResult(Source: hasSource ? new(McpSourceKind.Catalog, "sample.record", id, null) : null);

        var sources = await tool.GetSourcesAsync(new(McpSourceKind.Catalog, "sample.record", id), result, default);

        Assert.Empty(sources);
    }

    [Fact]
    public async Task SearchOmitsUnsupportedSourceKindsAndBlankTitles()
    {
        var tool = new SearchTool(Mock.Of<INgbSearchService>(), Mock.Of<IMcpNgbLinkResolver>());
        var id = Guid.NewGuid();
        var result = new SearchResult(
        [
            new("unsupported", "Unknown", "unsupported", "sample.record", id, null),
            new("untitled", " ", "catalog", "sample.record", id, null),
            new("valid", "Party", "catalog", "sample.record", id, null)
        ], false, []);

        var sources = await tool.GetSourcesAsync(new("Party"), result, default);

        Assert.Equal("Party", Assert.Single(sources).Title);
    }
}
