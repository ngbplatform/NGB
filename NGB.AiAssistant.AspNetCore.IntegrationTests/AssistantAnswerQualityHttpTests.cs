using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Moq;
using NGB.Application.Abstractions.Features;
using NGB.Application.Abstractions.Services;
using NGB.Attachments;
using NGB.Contracts.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Notes;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Mcp.AspNetCore;
using NGB.Notes;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed partial class AssistantHttpTests
{
    [Fact]
    public async Task RecordEvidenceKeepsAnUnknownTotalAndTheActualPageCriteria()
    {
        const string type = "pm.receivable_payment";
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(type, "Payment", EntityKind.Document));
        documents.Setup(value => value.GetPageAsync(type, It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<DocumentDto>(
                [new(Guid.NewGuid(), "Payment", new RecordPayload(), DocumentStatus.Posted, false)],
                0, 10, null, HasMore: true, NextCursor: "next-page"));
        await using var host = await Host.StartAsync(mode: "record-list", configure: services => services.AddScoped(_ => documents.Object));
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(value => value.Type == "text").Select(value => value.Text));
        var data = JsonSerializer.Deserialize<JsonElement>(answer).GetProperty("data");
        var page = data.GetProperty("documents");

        Assert.Contains(events, value => value.Type == "done");
        Assert.Equal(JsonValueKind.Null, page.GetProperty("total").ValueKind);
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal("next-page", page.GetProperty("nextCursor").GetString());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(10, data.GetProperty("criteria").GetProperty("limit").GetInt32());
        Assert.Contains("not a count", data.GetProperty("evidence").GetString());
        documents.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchEvidencePreservesIdentityAndCompletenessWithoutClaimingAnAggregate(bool truncated)
    {
        var id = Guid.NewGuid();
        var search = new Mock<INgbSearchService>(MockBehavior.Strict);
        search.Setup(value => value.SearchAsync("Example", It.IsAny<int>(), NgbSearchKind.Catalog,
                "sample.record", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NgbSearchResult([new("catalog", "sample.record", id, "Example")], truncated, ["reports"]));
        await using var host = await Host.StartAsync(mode: "search-evidence", configure: services => services.AddScoped(_ => search.Object));
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(value => value.Type == "text").Select(value => value.Text));
        var data = JsonSerializer.Deserialize<JsonElement>(answer).GetProperty("data");

        Assert.Contains(events, value => value.Type == "done");
        Assert.Equal(truncated, data.GetProperty("truncated").GetBoolean());
        Assert.Equal("reports", data.GetProperty("unavailableSources")[0].GetString());
        Assert.Equal("Example", data.GetProperty("criteria").GetProperty("query").GetString());
        Assert.Equal(id, Assert.Single(data.GetProperty("items").EnumerateArray()).GetProperty("recordId").GetGuid());
        Assert.Contains("ngb_list_reports", data.GetProperty("evidence").GetString());
        Assert.False(data.TryGetProperty("total", out _));
        Assert.Equal(id, Assert.Single(events, value => value.Type == "source").Source!.Id);
        search.VerifyAll();
    }

    [Fact]
    public async Task AssistantUsesTheSameDescriptionsAsTheAuthorizedMcpClient()
    {
        await using var host = await Host.StartAsync(mode: "custom-record");
        using var http = host.Client("alex");
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp")
        }, http));
        var tools = await mcp.ListToolsAsync();

        using var response = await http.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.Contains(events, item => item.Type == "done");
        var expected = tools.Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true
            && tool.ProtocolTool.Meta?[NgbMcpToolMetadata.CompatibilityOnly]?.GetValue<bool>() != true).ToArray();
        Assert.Equal(expected.Length, host.State.ToolDescriptions.Count);
        foreach (var tool in expected)
        {
            Assert.Equal(tool.Description, host.State.ToolDescriptions[tool.Name]);
        }
    }

    [NativeOllamaFact]
    public async Task NativeOllamaDiscoversAnAggregateFromAnUnrelatedInstalledApplication()
    {
        var count = Random.Shared.Next(200, 900);
        var definition = new ReportDefinitionDto("laboratory.calibration_summary", "Calibration Summary",
            Description: "The number of calibrated instruments across all laboratories.");
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([definition]);
        reports.Setup(value => value.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        reports.Setup(value => value.ExecuteAsync(definition.ReportCode, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportExecutionResponseDto(new ReportSheetDto(
                [new("calibrated_instruments", "Calibrated Instruments", "int32")],
                [new(ReportRowKind.Detail, [new(JsonSerializer.SerializeToElement(count), count.ToString(), "int32")])]),
                0, 1, 1, false, null));
        var budget = new Mock<IReportRequestAdmission>();
        budget.SetupGet(value => value.ExecutionTimeout).Returns(TimeSpan.FromSeconds(30));
        budget.Setup(value => value.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IDisposable>());
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            services.AddScoped(_ => reports.Object);
            services.AddScoped(_ => budget.Object);
        });
        using var http = host.Client("alex");
        http.Timeout = TimeSpan.FromMinutes(4);

        using var response = await http.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "How many calibrated instruments are there across all laboratories? Give only the count.")]));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        Assert.Contains(count.ToString(), answer);
        reports.Verify(value => value.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        reports.Verify(value => value.ExecuteAsync(definition.ReportCode, It.Is<ReportExecutionRequestDto>(request =>
            (request.Parameters == null || request.Parameters.Count == 0) && (request.Filters == null || request.Filters.Count == 0)),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaUsesTheInstalledTypesOutcomeWithoutAssumingACompletionDocument()
    {
        const string typeCode = "laboratory.inspection";
        var id = Guid.NewGuid();
        var metadata = new DocumentTypeMetadataDto(typeCode, "Inspection", EntityKind.Document,
            List: new ListMetadataDto([new("inspection_result", "Inspection Result", DataType.String)]));
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([metadata]);
        documents.Setup(value => value.GetTypeMetadataAsync(typeCode, It.IsAny<CancellationToken>())).ReturnsAsync(metadata);
        documents.Setup(value => value.GetByIdAsync(typeCode, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(id, "Inspection INS-0042", new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["inspection_result"] = JsonSerializer.SerializeToElement("Failed"),
                ["inspector_comment"] = JsonSerializer.SerializeToElement("Sample did not meet the required specification.")
            }), DocumentStatus.Posted, false));
        var scopes = new Mock<IRecordReadScopeFactory>();
        scopes.Setup(value => value.OpenAsync(It.IsAny<BusinessObjectRef>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            services.AddScoped(_ => documents.Object);
            services.AddScoped(_ => scopes.Object);
        });
        using var http = host.Client("alex");
        http.Timeout = TimeSpan.FromMinutes(4);

        using var response = await http.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Did this inspection pass? Explain what establishes the result.")], new("document", typeCode, id)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        Assert.Contains("fail", answer, StringComparison.OrdinalIgnoreCase);
        documents.Verify(value => value.GetByIdAsync(typeCode, id, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        documents.Verify(value => value.GetRelationshipGraphAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompactReportEvidencePreservesPagingScopeAndExactAmounts()
    {
        var amount = JsonSerializer.SerializeToElement(69511126.85m);
        var sheet = new ReportSheetDto(
            [new("amount", "Amount", "decimal")],
            [new(ReportRowKind.Group, [new(amount, "69,511,126.85", "decimal")],
                ChildrenPath: [JsonSerializer.SerializeToElement(7), JsonSerializer.SerializeToElement("category")])],
            new("Aging", "Selected lease"),
            HeaderRows: [new(ReportRowKind.Header, [new(Display: "October 2026")])]);
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetDefinitionAsync("pm.aging", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportDefinitionDto("pm.aging", "Aging"));
        reports.Setup(value => value.ExecuteAsync("pm.aging", It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportExecutionResponseDto(sheet, 0, 10, null, true, "next-page"));
        await using var host = await Host.StartAsync(mode: "report-execute", configure: services =>
            services.AddScoped(_ => reports.Object));
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var data = JsonSerializer.Deserialize<JsonElement>(answer).GetProperty("data");

        Assert.Contains(events, item => item.Type == "done");
        Assert.True(data.GetProperty("hasMore").GetBoolean());
        Assert.StartsWith("page-", data.GetProperty("nextCursor").GetString());
        Assert.False(data.TryGetProperty("total", out _));
        Assert.Equal("pm.aging", data.GetProperty("criteria").GetProperty("reportCode").GetString());
        Assert.Equal(10, data.GetProperty("criteria").GetProperty("request").GetProperty("limit").GetInt32());
        Assert.Equal("Selected lease", data.GetProperty("subtitle").GetString());
        Assert.Equal("October 2026", data.GetProperty("headerRows")[0].GetProperty("cells")[0].GetProperty("display").GetString());
        Assert.Equal(69511126.85m, data.GetProperty("rows")[0].GetProperty("cells")[0].GetProperty("value").GetDecimal());
        var childrenPath = data.GetProperty("rows")[0].GetProperty("childrenPath");
        Assert.Equal(7, childrenPath[0].GetInt32());
        Assert.Equal("category", childrenPath[1].GetString());
    }

    [NativeOllamaFact]
    public async Task NativeOllamaDoesNotSubstituteOneLeaseForAnAllLeaseTotal()
    {
        var definition = new ReportDefinitionDto("pm.receivables.open_items", "Open Items Report",
            Description: "Receivable open items for one lease, including unapplied payments.",
            Filters: [new("lease_id", "Lease", "uuid", IsRequired: true,
                Lookup: new DocumentLookupSourceDto(["pm.lease"]))]);
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([definition]);
        reports.Setup(value => value.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
            services.AddScoped(_ => reports.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What is the total unallocated payment amount across all leases? Give me only the amount and its currency, if known.")]));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        Assert.True(answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unable", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("can't", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("not available", StringComparison.OrdinalIgnoreCase), trace);
        Assert.True(answer.Length < 500, trace);
        Assert.DoesNotContain('?', answer);
        Assert.DoesNotContain("lease_id", answer);
        Assert.DoesNotContain("pm.receivables", answer);
        Assert.DoesNotContain("specify", answer, StringComparison.OrdinalIgnoreCase);
        reports.Verify(value => value.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        reports.Verify(value => value.ExecuteAsync(It.IsAny<string>(), It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaGreetingDoesNotRunTheCurrentPageReport()
    {
        await using var host = await Host.StartAsync(mode: "ollama");
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);
        var request = new AiAssistantRequest([new("user", "hi")], new("report", "pm.maintenance.queue"));

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", request);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var trace = string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        Assert.True(!trace.Contains("functionCall", StringComparison.OrdinalIgnoreCase), trace);
        Assert.Empty(host.State.ToolUsers);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaDoesNotClaimACompleteDateSelectionFromAPartialPage()
    {
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        var metadata = new DocumentTypeMetadataDto("pm.lease", "Lease", EntityKind.Document,
            List: new ListMetadataDto(
                [new("end_on_utc", "End On", DataType.Date)],
                [new("party_id", "Party", DataType.Guid), new("property_id", "Property", DataType.Guid)]));
        documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([metadata]);
        documents.Setup(value => value.GetTypeMetadataAsync("pm.lease", It.IsAny<CancellationToken>())).ReturnsAsync(metadata);
        var lease = new DocumentDto(Guid.NewGuid(), "Lease for unit 102", new RecordPayload(
            new Dictionary<string, JsonElement> { ["end_on_utc"] = JsonSerializer.SerializeToElement("2026-03-26") }), DocumentStatus.Posted, false);
        documents.Setup(value => value.GetPageAsync("pm.lease", It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<DocumentDto>([lease], 0, 10, null, HasMore: true, NextCursor: "next-page"));
        var search = new Mock<INgbSearchService>();
        search.Setup(value => value.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<NgbSearchKind?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NgbSearchResult([], false, []));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            services.AddScoped(_ => documents.Object);
            services.AddScoped(_ => search.Object);
            // Allow discovery, metadata and a bounded page read before the answer phase.
            services.Configure<AiAssistantOptions>(options => options.MaxToolCalls = 4);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "List all leases whose end date falls between October 1 and October 31, 2026.")], new("document", "pm.lease")));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        documents.Verify(value => value.GetTypeMetadataAsync("pm.lease", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        documents.Verify(value => value.GetPageAsync(It.IsAny<string>(), It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()), Times.AtMostOnce);
        Assert.False(Regex.IsMatch(answer,
            @"\b(there are no leases|no leases (have|end|exist|match|were found)|found no leases)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), answer + "\n" + trace);
        Assert.DoesNotContain("100", answer);
        Assert.True(answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unable", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("can't", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("not possible", StringComparison.OrdinalIgnoreCase), answer);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaUsesDisplayReferencesForNotesTimestampsAndAttachmentSizes()
    {
        var target = new BusinessObjectRef(BusinessObjectKind.Document, "pm.receivable_payment", Guid.NewGuid());
        var author = Guid.NewGuid();
        var created = new DateTime(2026, 5, 10, 20, 43, 34, DateTimeKind.Utc);
        var notes = new Mock<INoteService>(MockBehavior.Strict);
        notes.Setup(value => value.ListAsync(target, It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessObjectPage<NoteDto>(
                [new(Guid.NewGuid(), "Keep the date 2026-05-10 exactly as written.", 1, created, author, "Alex Carter", null, null)], null));
        var attachments = new Mock<IAttachmentService>(MockBehavior.Strict);
        attachments.Setup(value => value.ListAsync(target, It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessObjectPage<AttachmentDto>(
                [new(Guid.NewGuid(), "Screenshot 2026-10-02 at 5.46.52 PM.png", "image/png", 723275, created, author, "Alex Carter")], null));
        var features = new Mock<INgbFeatureService>();
        features.Setup(value => value.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            services.AddScoped(_ => notes.Object);
            services.AddScoped(_ => attachments.Object);
            services.AddScoped(_ => features.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Show notes with their text, authors and creation times. Also show attachment names and sizes.")],
            new("document", target.TypeCode, target.Id), "America/New_York"));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = JsonSerializer.Serialize(events) + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        foreach (var kind in new[] { "timestamp", "bytes", "text" })
        {
            Assert.True(events.Any(item => item.Value?.Kind == kind && answer.Contains(item.Value.Reference, StringComparison.Ordinal)), trace);
        }
        Assert.DoesNotContain("704 KB", answer);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaFindsAnExactDocumentNumberWithoutATypeHintOrUnrequestedAudit()
    {
        var id = Guid.NewGuid();
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        var metadata = new DocumentTypeMetadataDto("pm.payable_charge", "Payable Charge", EntityKind.Document);
        documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([metadata]);
        documents.Setup(value => value.GetTypeMetadataAsync(metadata.DocumentType, It.IsAny<CancellationToken>())).ReturnsAsync(metadata);
        documents.Setup(value => value.GetByIdAsync(metadata.DocumentType, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(id, "Payable Charge PC-2026-000021", new RecordPayload(
                new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(981.61m) }), DocumentStatus.Posted, false));
        var search = new Mock<INgbSearchService>(MockBehavior.Strict);
        search.Setup(value => value.SearchAsync("PC-2026-000021", It.IsAny<int>(), It.IsAny<NgbSearchKind?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NgbSearchResult([new("document", metadata.DocumentType, id, "Payable Charge PC-2026-000021")], true, []));
        var scopes = new Mock<IRecordReadScopeFactory>();
        scopes.Setup(value => value.OpenAsync(It.IsAny<BusinessObjectRef>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        var audit = new Mock<IAuditLogQueryService>(MockBehavior.Strict);
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            services.AddScoped(_ => documents.Object);
            services.AddScoped(_ => search.Object);
            services.AddScoped(_ => scopes.Object);
            services.AddScoped(_ => audit.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest([new("user", "find PC-2026-000021")]));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));

        Assert.True(events.All(item => item.Type != "error"), answer + "\n" + string.Join('\n', host.State.ModelInputs));
        Assert.Contains("PC-2026-000021", answer);
        Assert.Contains(events, item => item.Type == "source" && item.Source?.Id == id);
        audit.VerifyNoOtherCalls();
    }
}
