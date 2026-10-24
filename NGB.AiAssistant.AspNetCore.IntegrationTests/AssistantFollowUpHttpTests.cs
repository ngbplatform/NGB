using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Mcp;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed partial class AssistantHttpTests
{
    [Theory]
    [InlineData("true", "boolean")]
    [InlineData("10", "int32")]
    [InlineData("2.75", "decimal")]
    public async Task ReportParameterWireTypesAreValidatedWithoutChangingTheBusinessRequest(string value, string dataType)
    {
        var definition = new ReportDefinitionDto("sample.summary", "Summary",
            Parameters: [new("output_option", dataType, false)]);
        var reports = ReportMock(definition, _ => new ReportSheetDto([], []));
        await using var host = await Host.StartAsync(configure: services => AddReportReads(services, reports.Object));
        using var http = host.Client("alex");
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp")
        }, http));
        var arguments = new Dictionary<string, object?>
        {
            ["reportCode"] = definition.ReportCode,
            ["request"] = new { parameters = new { output_option = JsonSerializer.Deserialize<JsonElement>(value) } }
        };

        var invalid = await mcp.CallToolAsync(McpToolNames.ExecuteReport, arguments);

        Assert.True(invalid.IsError);
        reports.Verify(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<ReportExecutionRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);

        arguments["request"] = new { parameters = new { output_option = value } };
        var valid = await mcp.CallToolAsync(McpToolNames.ExecuteReport, arguments);

        Assert.NotEqual(true, valid.IsError);
        reports.Verify(x => x.ExecuteAsync(definition.ReportCode, It.Is<ReportExecutionRequestDto>(r =>
            r.Parameters != null && r.Parameters.Count == 1 && r.Parameters["output_option"] == value
            && r.Filters == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaCountsNamedRecordsWithAnExactServerTotal()
    {
        var metadata = new CatalogTypeMetadataDto("sample.contact", "Contacts", EntityKind.Catalog,
            List: new ListMetadataDto([new("display", "Name", DataType.String)], []));
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([metadata]);
        catalogs.Setup(x => x.GetTypeMetadataAsync(metadata.CatalogType, It.IsAny<CancellationToken>())).ReturnsAsync(metadata);
        catalogs.Setup(x => x.GetPageAsync(metadata.CatalogType, It.Is<PageRequestDto>(r =>
                r.IncludeTotal && r.Search == "Ava Adams"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>(
                [new(Guid.NewGuid(), "Ava Adams", new RecordPayload(), false, false)],
                0, 1, 37, true, "continuation"));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => services.AddScoped(_ => catalogs.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "How many contacts named Ava Adams do we have? Give the exact count.")], new("catalog", metadata.CatalogType)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(x => x.Type == "text").Select(x => x.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(x => x.Type != "error"), trace);
        Assert.True(answer.Contains("37", StringComparison.Ordinal), trace);
        catalogs.Verify(x => x.GetPageAsync(metadata.CatalogType, It.Is<PageRequestDto>(r =>
            r.IncludeTotal && r.Search == "Ava Adams"), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaReturnsTheVerifiedAuditLinkInsteadOfRepeatingHistory()
    {
        const string type = "sample.invoice";
        var id = Guid.NewGuid();
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(x => x.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(type, "Invoice", EntityKind.Document));
        documents.Setup(x => x.GetByIdAsync(type, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(id, "Invoice INV-0042", new RecordPayload(), DocumentStatus.Posted, false));
        var scopes = new Mock<IRecordReadScopeFactory>();
        scopes.Setup(x => x.OpenAsync(It.IsAny<BusinessObjectRef>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            services.AddScoped(_ => documents.Object);
            services.AddScoped(_ => scopes.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
        [
            new("user", "Who changed this document?"),
            new("assistant", "Alex Carter added a note to this invoice."),
            new("user", "Give me the link to the Audit Log of this document.")
        ], new("document", type, id)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(x => x.Type == "text").Select(x => x.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(x => x.Type != "error"), trace);
        var audit = Assert.Single(events, x => x.Type == "source" && x.Source?.View == "audit").Source!;
        Assert.Equal("Audit history: Invoice INV-0042", audit.Title);
        // The renderer replaces the model's link label with this verified display.
        Assert.Contains($"]({audit.Reference})", answer);
        Assert.DoesNotContain("Alex Carter", answer);
        documents.Verify(x => x.GetByIdAsync(type, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaUsesDeclaredRelationshipAndDateFiltersAsStrings()
    {
        var contactId = Guid.NewGuid();
        var metadata = new DocumentTypeMetadataDto("sample.agreement", "Agreements", EntityKind.Document,
            List: new ListMetadataDto([new("display", "Agreement", DataType.String)],
            [
                new("contact_id", "Contact", DataType.Guid, Description: "Agreements associated with this contact."),
                new("active_on", "Active on", DataType.Date, Description: "Agreements active on this date, yyyy-MM-dd.")
            ]));
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([metadata]);
        documents.Setup(x => x.GetTypeMetadataAsync(metadata.DocumentType, It.IsAny<CancellationToken>())).ReturnsAsync(metadata);
        documents.Setup(x => x.GetPageAsync(metadata.DocumentType, It.Is<PageRequestDto>(r =>
                r.Filters != null && r.Filters["contact_id"] == contactId.ToString("D") && r.Filters["active_on"] == "2026-10-18"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<DocumentDto>(
                [new(Guid.NewGuid(), "Agreement AG-0098", new RecordPayload(), DocumentStatus.Posted, false)], 0, 10, 1));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => services.AddScoped(_ => documents.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", $"List all agreements for contact {contactId:D} that are active on October 18, 2026.")]));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(x => x.Type == "text").Select(x => x.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(x => x.Type != "error"), trace);
        Assert.True(answer.Contains("AG-0098", StringComparison.Ordinal), trace);
        documents.Verify(x => x.GetPageAsync(metadata.DocumentType, It.Is<PageRequestDto>(r =>
            r.Filters != null && r.Filters["contact_id"] == contactId.ToString("D") && r.Filters["active_on"] == "2026-10-18"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaNarrowsThePreviousReportToTheRequestedCategory()
    {
        var definition = new ReportDefinitionDto("sample.trial_balance", "Trial Balance",
            Parameters: [new("from_utc", "date", true), new("to_utc", "date", true)]);
        var reports = ReportMock(definition, _ => new ReportSheetDto(
            [new("account", "Account", "string"), new("debit", "Debit", "decimal"), new("credit", "Credit", "decimal")],
            [
                new(ReportRowKind.Detail, [Cell("Asset"), Cell(881.25m), Cell(0m)]),
                new(ReportRowKind.Detail, [Cell("Income"), Cell(17.25m), Cell(193.50m)])
            ]));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddReportReads(services, reports.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);
        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
        [
            new("user", "Generate Trial Balance for October 1–18, 2026."),
            new("assistant", "Trial Balance for October 1–18, 2026: Asset debit 881.25, credit 0. Income debit 17.25, credit 193.50."),
            new("user", "only Income")
        ]));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(x => x.Type == "text").Select(x => x.Text));

        Assert.True(events.All(x => x.Type != "error"), answer + "\n" + string.Join('\n', host.State.ModelInputs));
        Assert.Contains("193.50", answer);
        Assert.DoesNotContain("881.25", answer);
        Assert.DoesNotContain("Asset", answer, StringComparison.OrdinalIgnoreCase);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaExpandsCanonicalChildrenWithoutChangingThePageDateRange()
    {
        var definition = new ReportDefinitionDto("sample.income_statement", "Income Statement",
            Capabilities: new ReportCapabilitiesDto(AllowsRowGroups: false, AllowsDetailFields: false),
            Parameters: [new("from_utc", "date", true), new("to_utc", "date", true)]);
        var reports = ReportMock(definition, request => request.GroupPath is { Count: > 0 }
            ? new ReportSheetDto([new("account", "Account", "string"), new("amount", "Amount", "decimal")],
                [new(ReportRowKind.Detail, [Cell("Parking"), Cell(17m)]), new(ReportRowKind.Detail, [Cell("Storage"), Cell(24m)])])
            : new ReportSheetDto([new("account", "Account", "string"), new("amount", "Amount", "decimal")],
                [new(ReportRowKind.Group, [Cell("Other Income"), Cell(41m)], ChildrenPath: [JsonSerializer.SerializeToElement(7)])]));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddReportReads(services, reports.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);
        var dates = new Dictionary<string, string> { ["from_utc"] = "2026-10-01", ["to_utc"] = "2026-10-18" };

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What does Other Income consist of in this report? Show the contributing accounts and amounts.")],
            new("report", definition.ReportCode, Parameters: dates)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(x => x.Type == "text").Select(x => x.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(x => x.Type != "error"), trace);
        Assert.True(answer.Contains("Parking", StringComparison.Ordinal), trace);
        Assert.True(answer.Contains("Storage", StringComparison.Ordinal), trace);
        reports.Verify(x => x.ExecuteAsync(definition.ReportCode, It.Is<ReportExecutionRequestDto>(r =>
            r.GroupPath != null && r.GroupPath.Count == 1 && r.GroupPath[0].GetInt32() == 7
            && r.Parameters!["from_utc"] == "2026-10-01" && r.Parameters["to_utc"] == "2026-10-18"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaRequestsCompleteTotalsWithoutRestrictingThePopulationToThePageRecord()
    {
        var definition = new ReportDefinitionDto("sample.balance_summary", "Balance Summary",
            Description: "Available credit across all agreements. Omit agreement_id for all agreements. Set totals_only=true to return one complete aggregate without listing agreements.",
            Parameters: [new("as_of_utc", "date", true), new("totals_only", "boolean", false)],
            Filters: [new("agreement_id", "Agreement", "uuid")]);
        var reports = ReportMock(definition, request => new ReportSheetDto(
            [new("available_credit", "Available credit", "decimal")],
            [new(ReportRowKind.Total, [Cell(request.Parameters?.GetValueOrDefault("totals_only") == "true" ? 7281.36m : 11m)])]));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddReportReads(services, reports.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What is the total available credit across all agreements as of October 18, 2026? Give only the total.")],
            new("document", "sample.agreement", Guid.NewGuid())));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(x => x.Type == "text").Select(x => x.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(x => x.Type != "error"), trace);
        Assert.True(answer.Replace(",", "", StringComparison.Ordinal).Contains("7281.36", StringComparison.Ordinal), trace);
        reports.Verify(x => x.ExecuteAsync(definition.ReportCode, It.Is<ReportExecutionRequestDto>(r =>
            r.Parameters != null && r.Parameters["as_of_utc"] == "2026-10-18" && r.Parameters["totals_only"] == "true"
            && (r.Filters == null || r.Filters.Count == 0)), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IAuthorizedReportReadService> ReportMock(
        ReportDefinitionDto definition, Func<ReportExecutionRequestDto, ReportSheetDto> sheet)
    {
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(x => x.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([definition]);
        reports.Setup(x => x.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        reports.Setup(x => x.ExecuteAsync(definition.ReportCode, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ReportExecutionRequestDto request, CancellationToken _) =>
            {
                var result = sheet(request);
                return new ReportExecutionResponseDto(result, 0, 10, result.Rows.Count, false, null);
            });
        return reports;
    }

    private static ReportCellDto Cell(object value)
        => new(JsonSerializer.SerializeToElement(value), value.ToString(), value is string ? "string" : "decimal");

    private static void AddReportReads(IServiceCollection services, IAuthorizedReportReadService reports)
    {
        var admission = new Mock<IReportRequestAdmission>();
        admission.SetupGet(x => x.ExecutionTimeout).Returns(TimeSpan.FromSeconds(30));
        admission.Setup(x => x.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IDisposable>());
        services.AddScoped(_ => reports);
        services.AddScoped(_ => admission.Object);
    }
}
