using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Application.Abstractions.Services;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task NumericEvidenceIsCheckedBeforePublishingTheHttpAnswer(bool repeatedError)
    {
        const string type = "sample.payment";
        var id = Guid.NewGuid();
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(type, "Payment", EntityKind.Document));
        documents.Setup(value => value.GetByIdAsync(type, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(id, "Payment PAY-0042", new RecordPayload(
                new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(227.5006m) }),
                DocumentStatus.Posted, false));
        var model = new WorkflowModel((round, _) => round switch
        {
            0 => ToolCall("payment", McpToolNames.GetRecord, new()
            {
                ["kind"] = "document", ["typeCode"] = type, ["id"] = id
            }),
            1 => Answer("The amount is $227.50."),
            _ => Answer(repeatedError ? "The amount is $227.5006." : "The amount is 227.5006.")
        });
        await using var host = await Host.StartAsync(configure: services =>
        {
            AddInvoiceReads(services, documents.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What is this payment's amount?")], new("document", type, id)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));

        Assert.DoesNotContain('$', answer);
        if (repeatedError)
        {
            Assert.Empty(answer);
            Assert.Equal("unverified_answer", Assert.Single(events, item => item.Type == "error").Code);
            Assert.DoesNotContain(events, item => item.Type == "done");
        }
        else
        {
            Assert.Equal("The amount is 227.5006.", answer);
            Assert.DoesNotContain(events, item => item.Type == "error");
            Assert.Contains(events, item => item.Type == "done");
        }
        documents.Verify(value => value.GetByIdAsync(type, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [NativeOllamaFact]
    public Task NativeOllamaKeepsTheRequestedMeasureAfterAQuarterClarification()
        => VerifyClarifiedTotalAsync(delegateChoice: false);

    [NativeOllamaFact]
    public Task NativeOllamaKeepsTheRequestedMeasureWhenAskedToChooseTheReport()
        => VerifyClarifiedTotalAsync(delegateChoice: true);

    private static async Task VerifyClarifiedTotalAsync(bool delegateChoice)
    {
        var definition = new ReportDefinitionDto("sample.unallocated", "Unallocated Payments",
            Description: "Total unallocated payment amount across all agreements for a period, with currency.",
            Parameters: [new("from_utc", "date", true), new("to_utc", "date", true)]);
        var reports = ReportMock(definition, _ => new ReportSheetDto(
            [new("amount", "Unallocated amount", "decimal"), new("currency", "Currency", "string")],
            [new(ReportRowKind.Total, [Cell(218.73m), Cell("USD")])]));
        var messages = new List<AiAssistantMessage>
        {
            new("user", "What is the total unallocated payment amount across all agreements? Give me only the amount and its currency, if known."),
            new("assistant", "Which time period should I use?"),
            new("user", "1 quarter 2026")
        };
        if (delegateChoice)
        {
            messages.Add(new("assistant", "Which report should I use to calculate it?"));
            messages.Add(new("user", "I don't know. Decide yourself."));
        }

        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddReportReads(services, reports.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(messages));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(answer.Contains("218.73", StringComparison.Ordinal), trace);
        Assert.True(answer.Contains("USD", StringComparison.Ordinal), trace);
        Assert.DoesNotContain('?', answer);
        reports.Verify(value => value.ExecuteAsync(definition.ReportCode, It.Is<ReportExecutionRequestDto>(request =>
            request.Parameters != null && request.Parameters["from_utc"] == "2026-01-01"
            && request.Parameters["to_utc"] == "2026-03-31" && request.Filters == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaDoesNotSubstituteASampleForAnUnavailableDateSelection()
    {
        const string type = "sample.agreement";
        var metadata = new DocumentTypeMetadataDto(type, "Agreements", EntityKind.Document,
            List: new ListMetadataDto(
                [new("end_on_utc", "End On", DataType.Date)],
                [new("property_id", "Property", DataType.Guid)]));
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([metadata]);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>())).ReturnsAsync(metadata);
        var end = DateTime.UtcNow.Date.AddMonths(1).ToString("yyyy-MM-dd");
        var agreement = new DocumentDto(Guid.NewGuid(), "Agreement AG-0098", new RecordPayload(
            new Dictionary<string, JsonElement> { ["end_on_utc"] = JsonSerializer.SerializeToElement(end) }),
            DocumentStatus.Posted, false);
        documents.Setup(value => value.GetPageAsync(type, It.Is<PageRequestDto>(request =>
                request.Search == null && request.Filters == null && request.Cursor == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<DocumentDto>([agreement], 0, 10, null, true, "more-agreements"));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => services.AddScoped(_ => documents.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Give me agreements expiring next month.")], new("document", type)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        output.WriteLine(trace);
        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("can't", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unable", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("not support", StringComparison.OrdinalIgnoreCase), trace);
        documents.Verify(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.False(answer.Contains("AG-0098", StringComparison.Ordinal), trace);
        documents.Verify(value => value.GetPageAsync(It.IsAny<string>(), It.IsAny<PageRequestDto>(),
            It.IsAny<CancellationToken>()), Times.AtMostOnce, trace);
        documents.Verify(value => value.GetPageAsync(It.IsAny<string>(), It.Is<PageRequestDto>(request =>
            request.Search != null || request.Filters != null || request.Cursor != null), It.IsAny<CancellationToken>()), Times.Never, trace);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaSelectsThePartyBalanceInsteadOfRequestingAnUnrelatedAccount()
    {
        var partyId = Guid.NewGuid();
        var accountCard = new ReportDefinitionDto("sample.account_card", "Account Card",
            Description: "Account activity and running balance.",
            Filters: [new("account_id", "Account", "uuid", IsRequired: true)]);
        var balance = new ReportDefinitionDto("sample.party_balance", "Party Outstanding Balance",
            Description: "Outstanding balance for the selected party, including vendors, as of the supplied date.",
            Parameters: [new("as_of_utc", "date", true)],
            Filters: [new("party_id", "Party", "uuid", IsRequired: true, Lookup: new CatalogLookupSourceDto("sample.party"))]);
        var reports = ReportMock(balance, _ => new ReportSheetDto(
            [new("outstanding", "Outstanding", "decimal"), new("currency", "Currency", "string")],
            [new(ReportRowKind.Total, [Cell(417.92m), Cell("USD")])]));
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([accountCard, balance]);
        reports.Setup(value => value.GetDefinitionAsync(accountCard.ReportCode, It.IsAny<CancellationToken>())).ReturnsAsync(accountCard);
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetTypeMetadataAsync("sample.party", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTypeMetadataDto("sample.party", "Parties", EntityKind.Catalog));
        catalogs.Setup(value => value.GetByIdAsync("sample.party", partyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogItemDto(partyId, "Atlantic Appliance", new RecordPayload(), false, false));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            AddReportReads(services, reports.Object);
            services.AddScoped(_ => catalogs.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What is this party's outstanding balance as of October 20, 2026?")], new("catalog", "sample.party", partyId)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(answer.Contains("417.92", StringComparison.Ordinal), trace);
        Assert.DoesNotContain("account_id", answer);
        Assert.False(answer.Contains('?'), trace);
        reports.Verify(value => value.ExecuteAsync(balance.ReportCode, It.Is<ReportExecutionRequestDto>(request =>
            request.Parameters != null && request.Parameters["as_of_utc"] == "2026-10-20"
            && request.Filters != null && request.Filters["party_id"].Value.GetGuid() == partyId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaExplainsAnUnavailableWriteWithoutBlamingUserPermissions()
    {
        var id = Guid.NewGuid();
        await using var host = await Host.StartAsync(mode: "ollama");
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Create and post a new charge for this agreement.")], new("document", "sample.agreement", id)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.DoesNotContain("permission", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(id.ToString(), answer);
        Assert.DoesNotContain('?', answer);
        Assert.True(answer.Length < 500, trace);
        Assert.Empty(host.State.ToolUsers);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaKeepsAnAllAgreementScopeWhenTheClarifiedTotalIsUnavailable()
    {
        var definition = new ReportDefinitionDto("sample.open_items", "Open Items",
            Description: "Outstanding charges and unallocated payments for one agreement only.",
            Filters: [new("agreement_id", "Agreement", "uuid", IsRequired: true,
                Lookup: new DocumentLookupSourceDto(["sample.agreement"]))]);
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([definition]);
        reports.Setup(value => value.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => services.AddScoped(_ => reports.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
        [
            new("user", "What is the total unallocated payment amount across all agreements? Amount and currency only."),
            new("assistant", "Which period and report should I use?"),
            new("user", "1 quarter 2026"),
            new("assistant", "Which report would you like?"),
            new("user", "I don't know. Decide yourself.")
        ]));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(answer.Contains("unallocated", StringComparison.OrdinalIgnoreCase), trace);
        Assert.True(answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("can't", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unable", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("not available", StringComparison.OrdinalIgnoreCase), trace);
        Assert.False(answer.Contains('?'), trace);
        reports.Verify(value => value.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        reports.Verify(value => value.ExecuteAsync(It.IsAny<string>(), It.IsAny<ReportExecutionRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaDoesNotTurnAPartialZeroIntoAPopulationBalance()
    {
        var definition = new ReportDefinitionDto("sample.balances", "Agreement Balances",
            Description: "Outstanding balances by agreement.");
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([definition]);
        reports.Setup(value => value.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        reports.Setup(value => value.ExecuteAsync(definition.ReportCode, It.Is<ReportExecutionRequestDto>(request => request.Cursor == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportExecutionResponseDto(new ReportSheetDto(
                [new("agreement", "Agreement", "string"), new("outstanding", "Outstanding", "decimal")],
                [new(ReportRowKind.Detail, [Cell("AG-0042"), Cell(0m)])]), 0, 1, null, true, "next-page"));
        reports.Setup(value => value.ExecuteAsync(definition.ReportCode, It.Is<ReportExecutionRequestDto>(request => request.Cursor != null),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddReportReads(services, reports.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What is the total outstanding balance across all agreements?")]));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("can't", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unable", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unknown", StringComparison.OrdinalIgnoreCase), trace);
        Assert.DoesNotContain("no outstanding", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total outstanding balance is 0", answer, StringComparison.OrdinalIgnoreCase);
        reports.Verify(value => value.ExecuteAsync(definition.ReportCode, It.Is<ReportExecutionRequestDto>(request => request.Cursor == null),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce, trace);
        // The answer must remain incomplete whether the model stops at the partial
        // result or attempts a continuation. Every continuation in this fixture fails.
    }

    [NativeOllamaFact]
    public async Task NativeOllamaReadsTheNewDocumentForACurrentRecordFollowUp()
    {
        const string type = "sample.invoice";
        var oldId = Guid.NewGuid();
        var currentId = Guid.NewGuid();
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(type, "Invoice", EntityKind.Document));
        documents.Setup(value => value.GetByIdAsync(type, currentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(currentId, "Invoice INV-0014", new RecordPayload(
                new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(1993m) }),
                DocumentStatus.Posted, false));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddInvoiceReads(services, documents.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
        [
            new("user", "What is this invoice's amount?") { Context = new("document", type, oldId) },
            new("assistant", "Invoice INV-0003 has an amount of 2795."),
            new("user", "And what is this invoice's amount?")
        ], new("document", type, currentId)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(answer.Contains("INV-0014", StringComparison.Ordinal), trace);
        Assert.True(answer.Replace(",", "", StringComparison.Ordinal).Contains("1993", StringComparison.Ordinal), trace);
        Assert.DoesNotContain("2795", answer.Replace(",", "", StringComparison.Ordinal));
        documents.Verify(value => value.GetByIdAsync(type, currentId, It.IsAny<CancellationToken>()), Times.Once);
        documents.Verify(value => value.GetByIdAsync(type, oldId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaPreservesPaymentPrecisionWithoutBorrowingCurrency()
    {
        const string type = "pm.receivable_payment";
        var oldId = Guid.NewGuid();
        var currentId = Guid.NewGuid();
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(type, "Receivable Payment", EntityKind.Document));
        documents.Setup(value => value.GetByIdAsync(type, currentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(currentId, "Receivable Payment RP-2026-000012", new RecordPayload(
                new Dictionary<string, JsonElement>
                {
                    ["amount"] = JsonSerializer.SerializeToElement(227.5006m),
                    ["received_on"] = JsonSerializer.SerializeToElement("2025-07-27")
                }),
                DocumentStatus.Posted, false));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddInvoiceReads(services, documents.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
        [
            new("user", "What are this payment's amount and received date?") { Context = new("document", type, oldId) },
            new("assistant", "This receivable payment has an amount of $2,795.00 and was received on September 16, 2025."),
            new("user", "And what are this payment's amount and received date?")
        ], new("document", type, currentId)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(answer.Contains("227.5006", StringComparison.Ordinal), trace);
        var receivedOn = Assert.Single(events, item => item.Value?.Value == "2025-07-27").Value!;
        Assert.True(answer.Contains(receivedOn.Reference, StringComparison.Ordinal), trace);
        Assert.DoesNotContain("$", answer);
        Assert.DoesNotContain("USD", answer);
        Assert.DoesNotContain("2795", answer.Replace(",", "", StringComparison.Ordinal));
        documents.Verify(value => value.GetByIdAsync(type, currentId, It.IsAny<CancellationToken>()), Times.Once);
        documents.Verify(value => value.GetByIdAsync(type, oldId, It.IsAny<CancellationToken>()), Times.Never);
    }
}
