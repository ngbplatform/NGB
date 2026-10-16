using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Application.Abstractions.Features;
using NGB.Application.Abstractions.Services;
using NGB.Attachments;
using NGB.Contracts.Accounting;
using NGB.Contracts.Attachments;
using NGB.Contracts.Audit;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Common;
using NGB.Contracts.Effects;
using NGB.Contracts.Graph;
using NGB.Contracts.Metadata;
using NGB.Contracts.Notes;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Contracts.WorkCenter;
using NGB.Core.AuditLog;
using NGB.Notes;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpBuiltInToolTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Id = Guid.Parse("1c811f20-b856-4bfd-bef8-5dad0e07ae93");

    [Fact]
    public async Task MetadataUsesInstalledDefinitionsAndCanonicalJournalIdentity()
    {
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new("test.party", "Parties", EntityKind.Catalog)]);
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        var journal = new DocumentTypeMetadataDto(AccountingDocumentTypeCodes.GeneralJournalEntry, "Journal", EntityKind.Document,
            Actions: [], Capabilities: new());
        documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([journal, new("test.invoice", "Invoices", EntityKind.Document)]);
        documents.Setup(value => value.GetTypeMetadataAsync(journal.DocumentType, It.IsAny<CancellationToken>())).ReturnsAsync(journal);
        using var host = Create(services =>
        {
            services.AddSingleton(catalogs.Object);
            services.AddSingleton(documents.Object);
        });
        var page = await Call(host, "ngb_list_metadata", new { limit = 2 });
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        var journalPage = await Call(host, "ngb_list_metadata", new { kind = "generalJournalEntry" });
        Assert.Equal("generalJournalEntry", Assert.Single(journalPage.GetProperty("items").EnumerateArray()).GetProperty("kind").GetString());
        var definition = await Call(host, "ngb_get_metadata", new { kind = "generalJournalEntry", typeCode = journal.DocumentType });
        Assert.Equal(JsonValueKind.Null, definition.GetProperty("document").GetProperty("capabilities").ValueKind);
        Assert.Empty(definition.GetProperty("document").GetProperty("actions").EnumerateArray());
        var invalid = await Assert.ThrowsAsync<McpReadException>(() => Call(host, "ngb_get_metadata",
            new { kind = "document", typeCode = journal.DocumentType }));
        Assert.Equal("invalid_input", invalid.Code);
        var capabilities = await Call(host, "ngb_get_capabilities", new { });
        Assert.True(capabilities.GetProperty("readOnly").GetBoolean());
        Assert.Contains("generalJournalEntry", capabilities.GetProperty("categories").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(15, capabilities.GetProperty("tools").GetArrayLength());
    }

    [Theory]
    [InlineData("catalog", "catalogs")]
    [InlineData("document", "documents")]
    [InlineData("generalJournalEntry", "generalJournalEntries")]
    public async Task RecordPagesPreserveExistingCursorsAndDoNotRequestTotals(string kind, string resultName)
    {
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetPageAsync("test.type", It.Is<PageRequestDto>(page => page.Cursor == "opaque"
            && page.Limit == 3 && !page.IncludeTotal && page.Offset == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>([], 0, 3, null, true, "next"));
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetPageAsync("test.type", It.Is<PageRequestDto>(page => page.Cursor == "opaque"
            && page.Limit == 3 && !page.IncludeTotal), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<DocumentDto>([], 0, 3, null, true, "next"));
        var journal = new Mock<IAuthorizedGeneralJournalEntryReadService>(MockBehavior.Strict);
        journal.Setup(value => value.GetPageAsync(0, 3, null, null, null, null, "opaque", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneralJournalEntryPageDto([], 0, 3, null, "next"));
        using var host = Create(services =>
        {
            services.AddSingleton(catalogs.Object);
            services.AddSingleton(documents.Object);
            services.AddSingleton(journal.Object);
        });
        var page = await Call(host, "ngb_list_records", new
        {
            kind,
            typeCode = kind == "generalJournalEntry" ? AccountingDocumentTypeCodes.GeneralJournalEntry : "test.type",
            limit = 3,
            cursor = "opaque"
        });
        Assert.Equal("next", page.GetProperty(resultName).GetProperty("nextCursor").GetString());
        Assert.Empty(page.GetProperty(resultName).GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("document")]
    public async Task DetailReadsAuthorizeBeforeOpeningReadOnlyScopeAndDisposeIt(string kind)
    {
        var sequence = new MockSequence();
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        var scopes = new Mock<IRecordReadScopeFactory>(MockBehavior.Strict);
        var lease = new Mock<IAsyncDisposable>();
        if (kind == "catalog")
            catalogs.InSequence(sequence).Setup(value => value.GetTypeMetadataAsync("test.type", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CatalogTypeMetadataDto("test.type", "Type", EntityKind.Catalog));
        else
            documents.InSequence(sequence).Setup(value => value.GetTypeMetadataAsync("test.type", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentTypeMetadataDto("test.type", "Type", EntityKind.Document));
        scopes.InSequence(sequence).Setup(value => value.OpenAsync(It.Is<BusinessObjectRef>(target => target.Id == Id),
            1000, 262144, It.IsAny<CancellationToken>())).ReturnsAsync(lease.Object);
        if (kind == "catalog")
            catalogs.InSequence(sequence).Setup(value => value.GetByIdAsync("test.type", Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CatalogItemDto(Id, "Party", new(), true, false));
        else
            documents.InSequence(sequence).Setup(value => value.GetByIdAsync("test.type", Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentDto(Id, "Invoice", new(), DocumentStatus.Posted, false));
        using var host = Create(services =>
        {
            services.AddSingleton(catalogs.Object);
            services.AddSingleton(documents.Object);
            services.AddSingleton(scopes.Object);
        });
        var record = await Call(host, "ngb_get_record", new { kind, typeCode = "test.type", id = Id });
        Assert.Equal(Id, record.GetProperty("source").GetProperty("id").GetGuid());
        Assert.Equal(kind, record.GetProperty("source").GetProperty("kind").GetString());
        lease.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task OversizedOrForbiddenDetailNeverInvokesTheDetailReader()
    {
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetTypeMetadataAsync("large", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTypeMetadataDto("large", "Large", EntityKind.Catalog));
        var scopes = new Mock<IRecordReadScopeFactory>(MockBehavior.Strict);
        scopes.Setup(value => value.OpenAsync(It.IsAny<BusinessObjectRef>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RecordReadLimitExceededException());
        using var host = Create(services =>
        {
            services.AddSingleton(catalogs.Object);
            services.AddSingleton(scopes.Object);
        });
        var error = await Assert.ThrowsAsync<McpReadException>(() => Call(host, "ngb_get_record", new { kind = "catalog", typeCode = "large", id = Id }));
        Assert.Equal("result_too_large", error.Code);
        catalogs.Verify(value => value.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FlowAndZeroEffectsPreserveSharedServiceResults()
    {
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetRelationshipGraphAsync("test.invoice", Id, 2, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipGraphDto([new("root", EntityKind.Document, "test.invoice", Id, "Invoice")], []));
        documents.Setup(value => value.GetEffectsAsync("test.invoice", Id, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEffectsDto([], [], []));
        using var host = Create(services => services.AddSingleton(documents.Object));
        var flow = await Call(host, "ngb_get_document_flow", new { typeCode = "test.invoice", id = Id });
        Assert.Single(flow.GetProperty("graph").GetProperty("nodes").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, flow.GetProperty("links").GetProperty("root").ValueKind);
        var effects = await Call(host, "ngb_get_document_effects", new { typeCode = "test.invoice", id = Id });
        Assert.Equal("complete", effects.GetProperty("completeness").GetString());
        Assert.Equal("result_local", effects.GetProperty("identifierScope").GetString());
        Assert.Empty(effects.GetProperty("effects").GetProperty("accountingEntries").EnumerateArray());
    }

    [Fact]
    public async Task ReportReadsPreserveEngineResponseAndAcquireExactlyOneSharedLease()
    {
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new("z", "Last"), new("a", "First")]);
        reports.Setup(value => value.GetDefinitionAsync("a", It.IsAny<CancellationToken>())).ReturnsAsync(new ReportDefinitionDto("a", "First"));
        reports.Setup(value => value.ExecuteAsync("a", It.Is<ReportExecutionRequestDto>(request => request.Cursor == "existing-cursor"
            && request.Limit == 2 && !request.DisablePaging), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportExecutionResponseDto(new([], []), 0, 2, null, true, "existing-next"));
        var budget = new Mock<IReportRequestAdmission>(MockBehavior.Strict);
        var lease = new Mock<IDisposable>();
        budget.Setup(value => value.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(lease.Object);
        budget.SetupGet(value => value.ExecutionTimeout).Returns(TimeSpan.FromSeconds(10));
        using var host = Create(services =>
        {
            services.AddSingleton(reports.Object);
            services.AddSingleton(budget.Object);
        });
        var list = await Call(host, "ngb_list_reports", new { limit = 1 });
        Assert.Equal("a", list.GetProperty("items")[0].GetProperty("reportCode").GetString());
        Assert.True(list.GetProperty("hasMore").GetBoolean());
        var definition = await Call(host, "ngb_get_report_definition", new { reportCode = "a" });
        Assert.Equal("First", definition.GetProperty("name").GetString());
        var result = await Call(host, "ngb_execute_report", new { reportCode = "a", request = new { limit = 2, cursor = "existing-cursor" } });
        Assert.Equal("existing-next", result.GetProperty("nextCursor").GetString());
        budget.Verify(value => value.AcquireAsync(It.IsAny<CancellationToken>()), Times.Once);
        lease.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task SearchExposesPartialSourcesWhileCompatibilitySearchFailsExplicitly()
    {
        var search = new Mock<INgbSearchService>(MockBehavior.Strict);
        search.Setup(value => value.SearchAsync("party", 20, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NgbSearchResult([new("catalog", "test.party", Id, "Party")], true, ["reports"]));
        using var host = Create(services => services.AddSingleton(search.Object));
        var result = await Call(host, "ngb_search", new { query = "party" });
        Assert.Equal($"catalog:test.party:{Id:D}", result.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal("reports", result.GetProperty("unavailableSources")[0].GetString());
        Assert.True(result.GetProperty("truncated").GetBoolean());
        var error = await Assert.ThrowsAsync<McpReadException>(() => Call(host, "search", new { query = "party" }));
        Assert.Equal("source_unavailable", error.Code);
    }

    [Theory]
    [InlineData("catalog", NgbSearchKind.Catalog)]
    [InlineData("document", NgbSearchKind.Document)]
    [InlineData("generalJournalEntry", NgbSearchKind.GeneralJournalEntry)]
    [InlineData("report", NgbSearchKind.Report)]
    public async Task SearchForwardsCanonicalFiltersToTheSharedReader(string kind, NgbSearchKind expectedKind)
    {
        var search = new Mock<INgbSearchService>(MockBehavior.Strict);
        search.Setup(value => value.SearchAsync("alpha", 3, expectedKind, "test.type", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NgbSearchResult([], false, []));
        using var host = Create(services => services.AddSingleton(search.Object));
        var result = await Call(host, "ngb_search", new { query = "alpha", limit = 3, kind, typeCode = "test.type" });
        Assert.Empty(result.GetProperty("items").EnumerateArray());
        search.VerifyAll();
    }

    [Fact]
    public async Task ContentReadsUseCanonicalParentAndNeverInvokeMutationsOrDownloads()
    {
        var target = new BusinessObjectRef(BusinessObjectKind.GeneralJournalEntry, AccountingDocumentTypeCodes.GeneralJournalEntry, Id);
        var notes = new Mock<INoteService>(MockBehavior.Strict);
        notes.Setup(value => value.ListAsync(target, 2, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessObjectPage<NoteDto>([new(Id, "Business note", 1, DateTime.UtcNow, Id, "Alex", null, null)], Id));
        var attachments = new Mock<IAttachmentService>(MockBehavior.Strict);
        attachments.Setup(value => value.ListAsync(target, 2, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessObjectPage<AttachmentDto>([new(Id, "invoice.pdf", "application/pdf", 100, DateTime.UtcNow, Id, "Alex")], null));
        var summaries = new Mock<IBusinessObjectContentSummaryService>(MockBehavior.Strict);
        summaries.Setup(value => value.GetAsync(target, It.IsAny<CancellationToken>())).ReturnsAsync(new BusinessObjectContentSummary(null, 1));
        var features = new Mock<INgbFeatureService>();
        features.Setup(value => value.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var host = Create(services =>
        {
            services.AddSingleton(notes.Object);
            services.AddSingleton(attachments.Object);
            services.AddSingleton(summaries.Object);
            services.AddSingleton(features.Object);
        });
        var request = new { target = new { kind = "generalJournalEntry", target.TypeCode, target.Id }, limit = 2 };
        var notePage = await Call(host, "ngb_list_notes", request);
        Assert.Equal("Business note", notePage.GetProperty("items")[0].GetProperty("text").GetString());
        var attachmentPage = await Call(host, "ngb_list_attachments", request);
        Assert.Equal("invoice.pdf", attachmentPage.GetProperty("items")[0].GetProperty("fileName").GetString());
        Assert.DoesNotContain("url", attachmentPage.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var summary = await Call(host, "ngb_get_content_summary", new { request.target });
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("attachments").ValueKind);
        features.Setup(value => value.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var disabled = await Assert.ThrowsAsync<McpReadException>(() => Call(host, "ngb_list_notes", request));
        Assert.Equal("unavailable", disabled.Code);
        notes.VerifyAll();
        attachments.VerifyAll();
    }

    [Fact]
    public async Task WorkCenterAndAuditReadsRetainPagingWithoutActionsOrMutations()
    {
        var work = new Mock<IWorkCenterQueryService>(MockBehavior.Strict);
        var item = new WorkCenterItemDto(Id, WorkCenterItemKind.Task, "task", "Review invoice", null,
            new("document", "test.invoice", Id, "Invoice", null), null, null, null,
            DateTime.UtcNow, null, false, false, null, null, "post", null, 1);
        work.Setup(value => value.GetItemsAsync(It.IsAny<WorkCenterQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkCenterPageDto([item], "continue", 30));
        work.Setup(value => value.GetSummaryAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkCenterSummaryDto(1, 1, 0, 0, 0, 1));
        var audit = new Mock<IAuditLogQueryService>(MockBehavior.Strict);
        audit.Setup(value => value.GetEntityAuditLogAsync(AuditEntityKind.Document, Id, null, null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditLogPageDto([], new(DateTime.UtcNow, Id), 50));
        using var host = Create(services =>
        {
            services.AddSingleton(work.Object);
            services.AddSingleton(audit.Object);
        });
        var page = await Call(host, "ngb_get_work_items", new { });
        Assert.Equal("continue", page.GetProperty("page").GetProperty("nextCursor").GetString());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("page").GetProperty("items")[0].GetProperty("primaryActionCode").ValueKind);
        var summary = await Call(host, "ngb_get_work_items", new { summaryOnly = true });
        Assert.Equal(1, summary.GetProperty("summary").GetProperty("attentionCount").GetInt32());
        var history = await Call(host, "ngb_get_audit_history", new { kind = "document", id = Id });
        Assert.Equal(Id, history.GetProperty("nextCursor").GetProperty("auditEventId").GetGuid());
    }

    [Fact]
    public async Task FetchReauthorizesSourceAndKeepsCompatibilityResponseAtRoot()
    {
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetDefinitionAsync("test.report", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportDefinitionDto("test.report", "Report"));
        var search = new Mock<INgbSearchService>(MockBehavior.Strict);
        search.Setup(value => value.SearchAsync("Report", 20, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NgbSearchResult([new("report", "test.report", null, "Report")], false, []));
        using var host = Create(services =>
        {
            services.AddSingleton(reports.Object);
            services.AddSingleton(search.Object);
        });
        var found = await Call(host, "search", new { query = "Report" });
        Assert.Equal("report:test.report", found.GetProperty("results")[0].GetProperty("id").GetString());
        var fetched = await Call(host, "fetch", new { id = "report:test.report" });
        Assert.Equal("Report", fetched.GetProperty("title").GetString());
        Assert.Contains("test.report", fetched.GetProperty("text").GetString());
        reports.Verify(value => value.GetDefinitionAsync("test.report", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task InvalidReportBoundsDoNotAcquireAdmission(int limit)
    {
        var budget = new Mock<IReportRequestAdmission>(MockBehavior.Strict);
        using var host = Create(services => services.AddSingleton(budget.Object));
        var error = await Assert.ThrowsAsync<McpReadException>(() => Call(host, "ngb_execute_report",
            new { reportCode = "test", request = new { limit } }));
        Assert.Equal("invalid_input", error.Code);
        budget.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BusyReportAdmissionAndExecutionTimeoutReleaseCapacity()
    {
        var budget = new Mock<IReportRequestAdmission>(MockBehavior.Strict);
        budget.Setup(value => value.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IDisposable?)null);
        using var host = Create(services => services.AddSingleton(budget.Object));
        var error = await Assert.ThrowsAsync<McpReadException>(() => Call(host, "ngb_execute_report",
            new { reportCode = "test", request = new { limit = 1 } }));
        Assert.Equal("capacity_exhausted", error.Code);
        var lease = new Mock<IDisposable>();
        budget.Setup(value => value.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(lease.Object);
        budget.SetupGet(value => value.ExecutionTimeout).Returns(TimeSpan.FromMilliseconds(10));
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.ExecuteAsync("test", It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .Returns(async (string code, ReportExecutionRequestDto request, CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new ReportExecutionResponseDto(new([], []), 0, 1, null, false, null);
            });
        using var timedHost = Create(services =>
        {
            services.AddSingleton(budget.Object);
            services.AddSingleton(reports.Object);
        });
        var timeout = await Assert.ThrowsAsync<McpReadException>(() => Call(timedHost, "ngb_execute_report",
            new { reportCode = "test", request = new { limit = 1 } }));
        Assert.Equal("timeout", timeout.Code);
        lease.Verify(value => value.Dispose(), Times.Once);
    }

    private static ServiceProvider Create(Action<IServiceCollection>? configure = null)
    {
        var services = McpTestServices.Create();
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<JsonElement> Call(ServiceProvider provider, string tool, object request)
    {
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<McpReadDispatcher>()
            .ExecuteAsync(tool, JsonSerializer.SerializeToElement(request, Json), default);
        return tool is "search" or "fetch" ? result : result.GetProperty("data");
    }
}
