using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Audit;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Mcp;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed partial class AssistantHttpTests
{
    [Fact]
    public async Task NullReadResultDoesNotAbortTheTurnOrBecomeEvidenceOfAbsence()
    {
        var candidate = new AiAssistantSource("document", "sample.invoice", Guid.NewGuid(), "Invoice INV-0042");
        var documents = InvoiceReads(candidate, allowed: true);
        var audit = new Mock<IAuditLogQueryService>(MockBehavior.Strict);
        audit.Setup(value => value.GetEntityAuditLogAsync(AuditEntityKind.Document, candidate.Id!.Value,
                It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditLogPageDto)null!);
        var model = new WorkflowModel((round, input) =>
        {
            if (round == 0)
                return ToolCall("audit", McpToolNames.GetAuditHistory, new()
                {
                    ["kind"] = "document", ["id"] = candidate.Id
                });
            if (round == 1)
            {
                Assert.Contains("empty_result", LatestToolData(input).GetRawText());
                return ToolCall("record", McpToolNames.GetRecord, new()
                {
                    ["kind"] = candidate.Kind, ["typeCode"] = candidate.TypeCode, ["id"] = candidate.Id
                });
            }

            return Answer($"[Audit history]({candidate.Reference}:audit)");
        });
        await using var host = await Host.StartAsync(configure: services =>
        {
            AddInvoiceReads(services, documents.Object);
            services.AddScoped(_ => audit.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains(events, item => item.Type == "done");
        Assert.Contains(events, item => item.Type == "source" && item.Source?.View == "audit");
        documents.Verify(value => value.GetByIdAsync(candidate.TypeCode, candidate.Id!.Value,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HistoricalLinksAreReauthorizedAndUseTheCurrentDisplay(bool allowed)
    {
        var candidate = new AiAssistantSource("document", "sample.invoice", Guid.NewGuid(), "Old display");
        var documents = InvoiceReads(candidate, allowed);
        var model = new WorkflowModel((_, _) => Answer($"[Old display]({candidate.Reference})"));
        await using var host = await Host.StartAsync(configure: services =>
        {
            AddInvoiceReads(services, documents.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
        [
            new("user", "Find invoice INV-0042."),
            new("assistant", "Invoice found.") { Sources = [candidate] },
            new("user", "Give me a link to open it.")
        ]));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains(events, item => item.Type == "done");
        if (allowed)
        {
            var source = Assert.Single(events, item => item.Type == "source").Source!;
            Assert.Equal(candidate.Reference, source.Reference);
            Assert.Equal("Invoice INV-0042", source.Title);
        }
        else
        {
            Assert.DoesNotContain(events, item => item.Type == "source");
        }

        documents.Verify(value => value.GetByIdAsync(candidate.TypeCode, candidate.Id!.Value,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaLinksThePreviouslyFoundRecordAfterThePageChanges()
    {
        var candidate = new AiAssistantSource("document", "sample.invoice", Guid.NewGuid(), "Invoice INV-0042");
        var documents = InvoiceReads(candidate, allowed: true);
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
            AddInvoiceReads(services, documents.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
        [
            new("user", "Find invoice INV-0042.") { Context = new("document", candidate.TypeCode, candidate.Id) },
            new("assistant", "Invoice INV-0042 found.") { Sources = [candidate] },
            new("user", "Give me a link to open the invoice you just found.")
        ], new("document", "sample.order", Guid.NewGuid())));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        Assert.Contains(events, item => item.Type == "source" && item.Source?.Reference == candidate.Reference);
        Assert.True(answer.Contains($"]({candidate.Reference})", StringComparison.Ordinal), trace);
        documents.Verify(value => value.GetByIdAsync(candidate.TypeCode, candidate.Id!.Value,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaDoesNotInventAReportRowOrItsDerivedMeaningFromAnEditor()
    {
        var invoice = new AiAssistantSource("document", "sample.invoice", Guid.NewGuid(), "Invoice INV-0042");
        var documents = InvoiceReads(invoice, allowed: true);
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
            AddInvoiceReads(services, documents.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Explain the negative Days Past Due value for this row. Is this charge overdue as of the report date?")],
            new("document", invoice.TypeCode, invoice.Id)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        Assert.True(answer.Contains("Days Past Due", StringComparison.OrdinalIgnoreCase), trace);
        Assert.True(answer.Contains('?') || answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("need", StringComparison.OrdinalIgnoreCase), trace);
        Assert.DoesNotContain("payment was applied", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adjustment reduced", answer, StringComparison.OrdinalIgnoreCase);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaClarifiesThePropertyWhenTheCurrentPageIsAContact()
    {
        var contactId = Guid.NewGuid();
        var contact = new CatalogTypeMetadataDto("sample.contact", "Contact", EntityKind.Catalog);
        var property = new CatalogTypeMetadataDto("sample.property", "Property", EntityKind.Catalog);
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([contact, property]);
        catalogs.Setup(value => value.GetTypeMetadataAsync(contact.CatalogType, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        catalogs.Setup(value => value.GetTypeMetadataAsync(property.CatalogType, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        catalogs.Setup(value => value.GetByIdAsync(contact.CatalogType, contactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogItemDto(contactId, "Ava Adams", new RecordPayload(), false, false));
        CatalogItemDto[] properties =
        [
            new(Guid.NewGuid(), "10 Pine Street", new RecordPayload(), false, false),
            new(Guid.NewGuid(), "20 Maple Street", new RecordPayload(), false, false)
        ];
        catalogs.Setup(value => value.GetPageAsync(property.CatalogType, It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, PageRequestDto request, CancellationToken _) =>
            {
                var matches = properties.Where(item => string.IsNullOrWhiteSpace(request.Search)
                    || item.Display!.Contains(request.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
                return new PageResponseDto<CatalogItemDto>(matches, 0, request.Limit, matches.Length);
            });
        var search = new Mock<INgbSearchService>(MockBehavior.Strict);
        search.Setup(value => value.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<NgbSearchKind?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NgbSearchResult([], false, []));
        var definition = new ReportDefinitionDto("sample.property_balances", "Property balances",
            Description: "Overdue balances for one property, ordered by outstanding amount descending.",
            Filters: [new("property_id", "Property", "uuid", IsRequired: true, Lookup: new CatalogLookupSourceDto(property.CatalogType))]);
        var reports = ReportMock(definition, _ => new ReportSheetDto([], []));
        var scopes = new Mock<IRecordReadScopeFactory>();
        scopes.Setup(value => value.OpenAsync(It.IsAny<BusinessObjectRef>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            services.AddScoped(_ => catalogs.Object);
            services.AddScoped(_ => scopes.Object);
            services.AddScoped(_ => search.Object);
            AddReportReads(services, reports.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Show overdue balances for this property, with the five largest outstanding amounts first.")],
            new("catalog", contact.CatalogType, contactId)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        Assert.True(answer.Contains("property", StringComparison.OrdinalIgnoreCase), trace);
        Assert.True(answer.Contains('?') || answer.Contains("specify", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("provide", StringComparison.OrdinalIgnoreCase), trace);
        reports.Verify(value => value.ExecuteAsync(It.IsAny<string>(), It.IsAny<ReportExecutionRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never, trace);
    }

    private static Mock<IAuthorizedDocumentReadService> InvoiceReads(AiAssistantSource source, bool allowed)
    {
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(source.TypeCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(source.TypeCode, "Invoice", EntityKind.Document));
        var read = documents.Setup(value => value.GetByIdAsync(source.TypeCode, source.Id!.Value, It.IsAny<CancellationToken>()));
        if (allowed)
            read.ReturnsAsync(new DocumentDto(source.Id!.Value, "Invoice INV-0042", new RecordPayload(), DocumentStatus.Posted, false));
        else
            read.ThrowsAsync(new UnauthorizedAccessException());
        return documents;
    }

    private static void AddInvoiceReads(IServiceCollection services, IAuthorizedDocumentReadService documents)
    {
        var scopes = new Mock<IRecordReadScopeFactory>();
        scopes.Setup(value => value.OpenAsync(It.IsAny<BusinessObjectRef>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        services.AddScoped(_ => documents);
        services.AddScoped(_ => scopes.Object);
    }
}
