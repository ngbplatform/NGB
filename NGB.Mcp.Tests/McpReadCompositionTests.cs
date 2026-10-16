using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Graph;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Mcp.Tools;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpReadCompositionTests
{
    [Fact]
    public async Task DocumentMetadataFilterExcludesTheDistinctJournalKind()
    {
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetAllMetadataAsync(default)).ReturnsAsync([
            new("test.invoice", "Invoice", EntityKind.Document),
            new(AccountingDocumentTypeCodes.GeneralJournalEntry, "Journal", EntityKind.Document)
        ]);
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetAllDefinitionsAsync(default)).ReturnsAsync([]);
        var reader = new MetadataReader(Mock.Of<IAuthorizedCatalogReadService>(), documents.Object, reports.Object);
        var result = await reader.ListAsync(McpSourceKind.Document, default);
        Assert.Equal("test.invoice", Assert.Single(result).TypeCode);
        var journals = await reader.ListAsync(McpSourceKind.GeneralJournalEntry, default);
        Assert.Equal(AccountingDocumentTypeCodes.GeneralJournalEntry, Assert.Single(journals).TypeCode);
        Assert.Empty(await reader.ListAsync(McpSourceKind.Report, default));
        var metadata = new GetMetadataTool(Mock.Of<IAuthorizedCatalogReadService>(), documents.Object, Mock.Of<IAuthorizedReportReadService>());
        var error = await Assert.ThrowsAsync<McpReadException>(() => metadata.ExecuteAsync(new((McpSourceKind)99, "test.invoice"), default));
        Assert.Equal("invalid_input", error.Code);
    }

    [Fact]
    public async Task OversizedDetailFailsBeforeMaterializingTheRecord()
    {
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetTypeMetadataAsync("test", default))
            .ReturnsAsync(new CatalogTypeMetadataDto("test", "Test", EntityKind.Catalog));
        var scopes = new Mock<IRecordReadScopeFactory>();
        scopes.Setup(value => value.OpenAsync(It.IsAny<BusinessObjectRef>(), 1000, 262144, default))
            .ThrowsAsync(new RecordReadLimitExceededException());
        var reader = new GetRecordTool(catalogs.Object, Mock.Of<IAuthorizedDocumentReadService>(),
            Mock.Of<IAuthorizedGeneralJournalEntryReadService>(), scopes.Object,
            Options.Create(new McpReadOptions()), Mock.Of<IMcpNgbLinkResolver>());

        var error = await Assert.ThrowsAsync<McpReadException>(() =>
            reader.ExecuteAsync(new(McpSourceKind.Catalog, "test", Guid.NewGuid()), default));

        Assert.Equal("result_too_large", error.Code);
        catalogs.Verify(value => value.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JournalPagesAcceptEmptyFiltersAndRejectUnsupportedFilters()
    {
        var journal = new Mock<IAuthorizedGeneralJournalEntryReadService>(MockBehavior.Strict);
        journal.Setup(value => value.GetPageAsync(0, 2, null, null, null, null, null, default))
            .ReturnsAsync(new GeneralJournalEntryPageDto([], 0, 2, null, null));
        var reader = new ListRecordsTool(Mock.Of<IAuthorizedCatalogReadService>(), Mock.Of<IAuthorizedDocumentReadService>(), journal.Object);
        var request = new RecordsRequest(McpSourceKind.GeneralJournalEntry, AccountingDocumentTypeCodes.GeneralJournalEntry, 2,
            Filters: new Dictionary<string, string>());
        var result = await reader.ExecuteAsync(request, default);
        Assert.Empty(result.GeneralJournalEntries!.Items);
        var error = await Assert.ThrowsAsync<McpReadException>(() => reader.ExecuteAsync(request with
        {
            Filters = new Dictionary<string, string> { ["unsupported"] = "value" }
        }, default));
        Assert.Equal("invalid_input", error.Code);
        journal.Verify(value => value.GetPageAsync(0, 2, null, null, null, null, null, default), Times.Once);
    }

    [Fact]
    public async Task FailedDetailReadStillDisposesItsReadOnlySnapshot()
    {
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetTypeMetadataAsync("test", default)).ReturnsAsync(new CatalogTypeMetadataDto("test", "Test", EntityKind.Catalog));
        catalogs.Setup(value => value.GetByIdAsync("test", It.IsAny<Guid>(), default)).ThrowsAsync(new IOException("provider failure"));
        var lease = new Mock<IAsyncDisposable>();
        var scopes = new Mock<IRecordReadScopeFactory>();
        scopes.Setup(value => value.OpenAsync(It.IsAny<BusinessObjectRef>(), 1000, 262144, default)).ReturnsAsync(lease.Object);
        var reader = new GetRecordTool(catalogs.Object, Mock.Of<IAuthorizedDocumentReadService>(), Mock.Of<IAuthorizedGeneralJournalEntryReadService>(),
            scopes.Object, Options.Create(new McpReadOptions()), Mock.Of<IMcpNgbLinkResolver>());
        await Assert.ThrowsAsync<IOException>(() => reader.ExecuteAsync(new(McpSourceKind.Catalog, "test", Guid.NewGuid()), default));
        lease.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task FlowAndSearchUseCanonicalCatalogAndReportLinks()
    {
        var id = Guid.NewGuid();
        var links = new ConfiguredMcpNgbLinkResolver(new Uri("https://ngb.example"), new Dictionary<string, string>
        {
            ["catalog"] = "/catalogs/{typeCode}/{id}",
            ["report"] = "/reports/{typeCode}"
        });
        var documents = new Mock<IAuthorizedDocumentReadService>();
        documents.Setup(value => value.GetRelationshipGraphAsync("test.invoice", id, 2, 100, default))
            .ReturnsAsync(new RelationshipGraphDto([new("party", EntityKind.Catalog, "test.party", id, "Party")], []));
        var flow = await new FlowTool(documents.Object, links).ExecuteAsync(new("test.invoice", id), default);
        Assert.Equal($"https://ngb.example/catalogs/test.party/{id:D}", flow.Links["party"]);
        var search = new Mock<INgbSearchService>();
        search.Setup(value => value.SearchAsync("balances", 20, null, null, default))
            .ReturnsAsync(new NgbSearchResult([new("report", "test.balances", null, "Balances")], false, []));
        var found = await new SearchTool(search.Object, links).ExecuteAsync(new("balances"), default);
        Assert.Equal("https://ngb.example/reports/test.balances", Assert.Single(found.Items).Url);
        var reports = new Mock<IAuthorizedReportReadService>();
        reports.Setup(value => value.GetDefinitionAsync("test.balances", default)).ReturnsAsync(new ReportDefinitionDto("test.balances", "Balances"));
        var services = McpTestServices.Create();
        services.AddSingleton(reports.Object);
        services.AddSingleton<IMcpNgbLinkResolver>(links);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var fetched = await scope.ServiceProvider.GetRequiredService<McpReadDispatcher>()
            .ExecuteAsync("fetch", JsonSerializer.SerializeToElement(new { id = "report:test.balances" }), default);
        Assert.Equal("https://ngb.example/reports/test.balances", fetched.GetProperty("url").GetString());
    }
}
