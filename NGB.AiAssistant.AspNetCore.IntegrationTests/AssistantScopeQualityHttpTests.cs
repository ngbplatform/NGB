using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Audit;
using NGB.Contracts.Common;
using NGB.Contracts.Graph;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Mcp;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed partial class AssistantHttpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AssistantExposesActionableReadErrorsWithoutInventingBusinessResults(bool invalidArguments)
    {
        const string code = "sample.activity";
        var definition = new ReportDefinitionDto(code, "Activity");
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetDefinitionAsync(code, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        reports.Setup(value => value.ExecuteAsync(code, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ReportExecutionRequestDto _, CancellationToken _) =>
            {
                if (!invalidArguments)
                    throw new InvalidOperationException("The report read failed.");

                return new ReportExecutionResponseDto(new ReportSheetDto([], []), 0, 10, 0, false, null);
            });
        var model = new WorkflowModel((round, input) =>
        {
            if (round == 0)
            {
                var arguments = new Dictionary<string, object?>
                {
                    ["reportCode"] = code,
                    ["request"] = new { limit = 10 }
                };
                if (invalidArguments)
                    arguments["limit"] = "100";

                return ToolCall("first", McpToolNames.ExecuteReport, arguments);
            }

            if (round == 1)
            {
                var error = LatestToolData(input).GetProperty("data");
                Assert.Equal(invalidArguments ? "invalid_input" : "internal_error", error.GetProperty("error").GetString());
                Assert.False(error.TryGetProperty("sheet", out _));
                if (invalidArguments)
                {
                    Assert.Contains("$.limit", error.GetProperty("message").GetString());
                    Assert.True(error.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("request", out _));
                    Assert.Contains("Correct the indicated path", error.GetProperty("recovery").GetString());
                    return ToolCall("corrected", McpToolNames.ExecuteReport, new()
                    {
                        ["reportCode"] = code,
                        ["request"] = new { limit = 10 }
                    });
                }

                Assert.Equal(JsonValueKind.Null, error.GetProperty("inputSchema").ValueKind);
                Assert.Contains("Do not retry", error.GetProperty("recovery").GetString());
            }

            return Answer(invalidArguments ? "The corrected read succeeded." : "The requested result remains unavailable.");
        });
        await using var host = await Host.StartAsync(configure: services =>
        {
            AddReportReads(services, reports.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains(events, item => item.Type == "done");
        reports.Verify(value => value.ExecuteAsync(code, It.IsAny<ReportExecutionRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssistantMetadataPreservesTheAbsenceOfADocumentNumber()
    {
        const string type = "sample.agreement";
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(type, "Agreement", EntityKind.Document,
                Presentation: new DocumentPresentationDto(HasNumber: false, ComputedDisplay: true)));
        var model = new WorkflowModel((round, input) =>
        {
            if (round == 0)
            {
                return ToolCall("metadata", McpToolNames.GetMetadata, new()
                {
                    ["kind"] = "document", ["typeCode"] = type
                });
            }

            Assert.False(LatestToolData(input).GetProperty("data").GetProperty("hasNumber").GetBoolean());
            return Answer("Agreements are identified by their display, without a document number.");
        });
        await using var host = await Host.StartAsync(configure: services =>
        {
            services.AddScoped(_ => documents.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains(events, item => item.Type == "done");
        documents.VerifyAll();
    }

    [NativeOllamaFact]
    public async Task NativeOllamaDoesNotOfferTheCurrentRecordWhenAnExplicitNumberIsMissing()
    {
        var source = new AiAssistantSource("document", "sample.invoice", Guid.NewGuid(), "Invoice INV-0014");
        var documents = InvoiceReads(source, allowed: true);
        var search = new Mock<INgbSearchService>(MockBehavior.Strict);
        search.Setup(value => value.SearchAsync("INV-2099-999999", It.IsAny<int>(), It.IsAny<NgbSearchKind?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NgbSearchResult([], false, []));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            AddInvoiceReads(services, documents.Object);
            services.AddScoped(_ => search.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
        [
            new("user", "What is this invoice's amount?"),
            new("assistant", "Invoice INV-0014 has an amount of 153.9794.") { Sources = [source] },
            new("user", "Find invoice INV-2099-999999 and provide its amount and a link.")
        ], new(source.Kind, source.TypeCode, source.Id)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        Assert.True(answer.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("could not find", StringComparison.OrdinalIgnoreCase), trace);
        Assert.DoesNotContain("INV-0014", answer);
        Assert.DoesNotContain("153.98", answer);
        Assert.DoesNotContain("153.9794", answer);
        Assert.DoesNotContain("ngb-source:", answer);
        Assert.True(answer.Length < 400, trace);
        documents.Verify(value => value.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never, trace);
        search.VerifyAll();
    }

    [NativeOllamaFact]
    public async Task NativeOllamaDoesNotSelectAMaintenanceRequestThroughASharedProperty()
    {
        const string type = "sample.payment";
        var id = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var paymentMetadata = new DocumentTypeMetadataDto(type, "Payment", EntityKind.Document);
        var requestMetadata = new DocumentTypeMetadataDto("sample.request", "Maintenance Request", EntityKind.Document);
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([paymentMetadata, requestMetadata]);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paymentMetadata);
        documents.Setup(value => value.GetTypeMetadataAsync(requestMetadata.DocumentType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requestMetadata);
        documents.Setup(value => value.GetByIdAsync(type, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(id, "Payment PAY-0042", new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["property_id"] = JsonSerializer.SerializeToElement(propertyId)
            }), DocumentStatus.Posted, false));
        documents.Setup(value => value.GetByIdAsync(requestMetadata.DocumentType, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(requestId, "Maintenance Request MR-0041", new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["property_id"] = JsonSerializer.SerializeToElement(propertyId),
                ["outcome"] = JsonSerializer.SerializeToElement("Completed")
            }), DocumentStatus.Posted, false));
        documents.Setup(value => value.GetRelationshipGraphAsync(type, id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipGraphDto(
                [new("payment", EntityKind.Document, type, id, "Payment PAY-0042"),
                    new("property", EntityKind.Catalog, "sample.property", propertyId, "100 Hudson Ave")],
                [new("payment", "property", "property_id", "Property")]));
        var search = new Mock<INgbSearchService>(MockBehavior.Strict);
        search.Setup(value => value.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<NgbSearchKind?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NgbSearchResult([new("document", "sample.request", requestId, "Request MR-0041")], false, []));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            AddInvoiceReads(services, documents.Object);
            services.AddScoped(_ => search.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Has the work for this maintenance request been completed? Explain what confirms its status.")],
            new("document", type, id)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        output.WriteLine(trace);
        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(answer.Contains("payment", StringComparison.OrdinalIgnoreCase), trace);
        Assert.True(answer.Contains('?') || answer.Contains("specify", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("provide", StringComparison.OrdinalIgnoreCase), trace);
        Assert.DoesNotContain("MR-0041", answer);
        Assert.True(answer.Length < 500, trace);
        search.Verify(value => value.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<NgbSearchKind?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never, trace);
        documents.Verify(value => value.GetByIdAsync(requestMetadata.DocumentType, requestId,
            It.IsAny<CancellationToken>()), Times.Never, trace);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaUsesADirectReferenceToTheRequestedRecord()
    {
        const string paymentType = "sample.payment";
        const string agreementType = "sample.agreement";
        var paymentId = Guid.NewGuid();
        var agreementId = Guid.NewGuid();
        var agreementField = new FieldMetadataDto("agreement_id", "Lease", DataType.Guid, UiControl.Lookup,
            Lookup: new DocumentLookupSourceDto([agreementType]));
        var paymentMetadata = new DocumentTypeMetadataDto(paymentType, "Payment", EntityKind.Document,
            Form: new FormMetadataDto([new("Main", [new([agreementField])])]));
        var agreementMetadata = new DocumentTypeMetadataDto(agreementType, "Lease", EntityKind.Document);
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([paymentMetadata, agreementMetadata]);
        documents.Setup(value => value.GetTypeMetadataAsync(paymentType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paymentMetadata);
        documents.Setup(value => value.GetTypeMetadataAsync(agreementType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agreementMetadata);
        documents.Setup(value => value.GetByIdAsync(paymentType, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(paymentId, "Payment PAY-0042", new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["agreement_id"] = JsonSerializer.SerializeToElement(agreementId)
            }), DocumentStatus.Posted, false));
        documents.Setup(value => value.GetByIdAsync(agreementType, agreementId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(agreementId, "100 Hudson Ave #114", new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["rent_amount"] = JsonSerializer.SerializeToElement(1405m)
            }), DocumentStatus.Posted, false));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddInvoiceReads(services, documents.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What is the rent amount for this lease?")], new("document", paymentType, paymentId)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(answer.Replace(",", "", StringComparison.Ordinal).Contains("1405", StringComparison.Ordinal), trace);
        Assert.DoesNotContain('?', answer);
        documents.Verify(value => value.GetByIdAsync(agreementType, agreementId,
            It.IsAny<CancellationToken>()), Times.Once, trace);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaDoesNotInferNoDebtFromAnEmptyDirectBuildingLookup()
    {
        const string propertyType = "sample.property";
        const string agreementType = "sample.agreement";
        var buildingId = Guid.NewGuid();
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetTypeMetadataAsync(propertyType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTypeMetadataDto(propertyType, "Properties", EntityKind.Catalog));
        catalogs.Setup(value => value.GetByIdAsync(propertyType, buildingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogItemDto(buildingId, "Liberty Building", new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["kind"] = JsonSerializer.SerializeToElement("Building")
            }), false, false));
        var metadata = new DocumentTypeMetadataDto(agreementType, "Agreements", EntityKind.Document,
            List: new ListMetadataDto([new("property_id", "Property", DataType.Guid)],
                [new("property_id", "Property", DataType.Guid, Lookup: new CatalogLookupSourceDto(propertyType))]));
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([metadata]);
        documents.Setup(value => value.GetTypeMetadataAsync(agreementType, It.IsAny<CancellationToken>())).ReturnsAsync(metadata);
        documents.Setup(value => value.GetPageAsync(agreementType, It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<DocumentDto>([], 0, 10, 0, false));
        var definition = new ReportDefinitionDto("sample.open_items", "Open Items",
            Description: "Outstanding charges for one agreement.",
            Filters: [new("agreement_id", "Agreement", "uuid", IsRequired: true,
                Lookup: new DocumentLookupSourceDto([agreementType]))]);
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([definition]);
        reports.Setup(value => value.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            AddInvoiceReads(services, documents.Object);
            services.AddScoped(_ => catalogs.Object);
            services.AddScoped(_ => reports.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Show overdue balances for this building, with the five largest outstanding amounts first.")],
            new("catalog", propertyType, buildingId)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(answer.Contains("cannot", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("can't", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("unable", StringComparison.OrdinalIgnoreCase), trace);
        Assert.DoesNotContain("no overdue", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no outstanding", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no associated", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0.00", answer);
        Assert.DoesNotContain('?', answer);
        Assert.True(answer.Length < 600, trace);
        reports.Verify(value => value.ExecuteAsync(It.IsAny<string>(), It.IsAny<ReportExecutionRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never, trace);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaPreservesAuditFilenamesAndUsesTheDisplayTimeZone()
    {
        var source = new AiAssistantSource("document", "sample.invoice", Guid.NewGuid(), "Invoice INV-0042");
        var documents = InvoiceReads(source, allowed: true);
        const string filename = "Screenshot 2026-10-02 at 5.46.52 PM.png";
        var audit = new Mock<IAuditLogQueryService>(MockBehavior.Strict);
        audit.Setup(value => value.GetEntityAuditLogAsync(AuditEntityKind.Document, source.Id!.Value,
                It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditLogPageDto(
            [
                new(Guid.NewGuid(), (short)AuditEntityKind.Document, source.Id!.Value, "attachments.upload_completed",
                    new(Guid.NewGuid(), "Alex Carter", null), new DateTime(2026, 10, 4, 14, 28, 23, DateTimeKind.Utc),
                    null, null, [new("attachment.file_name", null, JsonSerializer.Serialize(filename))])
            ], null, 50));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            AddInvoiceReads(services, documents.Object);
            services.AddScoped(_ => audit.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Who changed this document, when, and what changed? Show the available history in a table.")],
            new(source.Kind, source.TypeCode, source.Id), "America/New_York"));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + JsonSerializer.Serialize(events) + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        var literal = Assert.Single(events, item => item.Value?.Value == filename).Value!;
        var timestamp = Assert.Single(events, item => item.Value?.Kind == "timestamp").Value!;
        Assert.True(answer.Contains(literal.Reference, StringComparison.Ordinal), trace);
        Assert.True(answer.Contains(timestamp.Reference, StringComparison.Ordinal), trace);
        Assert.Contains("Alex Carter", answer);
        Assert.DoesNotContain("UTC", answer);
        Assert.DoesNotContain("10/02/2026", answer);
        Assert.DoesNotContain(source.Id!.Value.ToString(), answer);
        audit.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditEvidenceDistinguishesMissingActorsAndUnverifiedContinuation(bool filteredPage)
    {
        var source = new AiAssistantSource("document", "sample.invoice", Guid.NewGuid(), "Invoice INV-0042");
        var eventId = Guid.NewGuid();
        var occurred = new DateTime(2026, 10, 17, 19, 19, 57, DateTimeKind.Utc);
        var audit = new Mock<IAuditLogQueryService>(MockBehavior.Strict);
        audit.Setup(value => value.GetEntityAuditLogAsync(AuditEntityKind.Document, source.Id!.Value,
                null, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditLogPageDto(filteredPage ? [] :
                [new(eventId, (short)AuditEntityKind.Document, source.Id!.Value, "document.post",
                    null, occurred, null, null, [])], new(occurred, eventId), 50));
        var model = new WorkflowModel((round, input) =>
        {
            if (round == 0)
            {
                return ToolCall("audit", McpToolNames.GetAuditHistory, new()
                {
                    ["kind"] = "document",
                    ["id"] = source.Id
                });
            }

            var data = LatestToolData(input).GetProperty("data");
            if (filteredPage)
                Assert.Empty(data.GetProperty("items").EnumerateArray());
            else
                Assert.False(data.GetProperty("items")[0].GetProperty("actorRecorded").GetBoolean());
            Assert.Equal(eventId, data.GetProperty("nextCursor").GetProperty("auditEventId").GetGuid());
            Assert.False(data.GetProperty("endReached").GetBoolean());
            Assert.Contains("not proof", data.GetProperty("paging").GetString());
            return Answer("The event's author is unknown; further events have not been checked.");
        });
        await using var host = await Host.StartAsync(configure: services =>
        {
            services.AddScoped(_ => audit.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.Contains(events, item => item.Type == "done");
        audit.VerifyAll();
    }

    [NativeOllamaFact]
    public async Task NativeOllamaKeepsAuditAuthorsWithTheirEventsAndReadsToTheEnd()
    {
        var source = new AiAssistantSource("document", "sample.invoice", Guid.NewGuid(), "Invoice INV-0042");
        var documents = InvoiceReads(source, allowed: true);
        var createdId = Guid.NewGuid();
        var created = new DateTime(2026, 5, 10, 20, 42, 30, DateTimeKind.Utc);
        var noted = new DateTime(2026, 10, 17, 19, 19, 57, DateTimeKind.Utc);
        var audit = new Mock<IAuditLogQueryService>(MockBehavior.Strict);
        audit.Setup(value => value.GetEntityAuditLogAsync(AuditEntityKind.Document, source.Id!.Value,
                null, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditLogPageDto(
            [
                new(Guid.NewGuid(), (short)AuditEntityKind.Document, source.Id!.Value, "notes.created",
                    new(Guid.NewGuid(), "Alex Carter", null), noted, null, null,
                    [new("note.text", null, JsonSerializer.Serialize("Note #3"))]),
                new(createdId, (short)AuditEntityKind.Document, source.Id!.Value, "document.create_draft",
                    null, created, null, null, [new("amount", null, "2795.00")])
            ], new(created, createdId), 50));
        audit.Setup(value => value.GetEntityAuditLogAsync(AuditEntityKind.Document, source.Id!.Value,
                created, createdId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditLogPageDto([], null, 50));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            AddInvoiceReads(services, documents.Object);
            services.AddScoped(_ => audit.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Who changed this document, when, and what changed? Show the available history in a table.")],
            new(source.Kind, source.TypeCode, source.Id), "America/New_York"));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + JsonSerializer.Serialize(events) + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        var creationTime = Assert.Single(events, item => item.Value?.Value == "2026-05-10T20:42:30Z").Value!;
        var noteTime = Assert.Single(events, item => item.Value?.Value == "2026-10-17T19:19:57Z").Value!;
        Assert.True(answer.Contains(creationTime.Reference, StringComparison.Ordinal), trace);
        Assert.True(answer.Contains(noteTime.Reference, StringComparison.Ordinal), trace);
        var creationRow = Assert.Single(answer.Split('\n'), line => line.Contains(creationTime.Reference, StringComparison.Ordinal));
        var noteRow = Assert.Single(answer.Split('\n'), line => line.Contains(noteTime.Reference, StringComparison.Ordinal));
        Assert.DoesNotContain("Alex Carter", creationRow);
        Assert.Contains("Alex Carter", noteRow);
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(answer, @"\d{1,2}/\d{1,2}/\d{4}\s+\{\{ngb-value:"), trace);
        Assert.DoesNotContain("partial", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("further events remain", answer, StringComparison.OrdinalIgnoreCase);
        audit.VerifyAll();
    }

    [NativeOllamaFact]
    public async Task NativeOllamaDoesNotExplainDuplicateChargeRowsAsDistinctMonths()
    {
        var partyId = Guid.NewGuid();
        var definition = new ReportDefinitionDto("sample.outstanding", "Outstanding Charges",
            Description: "Remaining outstanding charges and their total for the selected customer.",
            Parameters: [new("as_of_utc", "date", true)],
            Filters: [new("party_id", "Customer", "uuid", IsRequired: true,
                Lookup: new CatalogLookupSourceDto("sample.party"))]);
        var rows = Enumerable.Range(1, 2).Select(index => new ReportSheetRowDto(ReportRowKind.Detail,
            [Cell($"CH-{index:D4}"), Cell("253 Observer Ave #382"), Cell("2026-05-04"), Cell(3093m)])).ToList();
        rows.Add(new(ReportRowKind.Total, [Cell("Total"), Cell(""), Cell(""), Cell(6186m)]));
        var reports = ReportMock(definition, _ => new ReportSheetDto(
            [new("charge", "Charge", "string"), new("property", "Property", "string"),
                new("due_on", "Due On", "date"), new("outstanding", "Outstanding", "decimal")], rows));
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetTypeMetadataAsync("sample.party", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTypeMetadataDto("sample.party", "Customer", EntityKind.Catalog));
        catalogs.Setup(value => value.GetByIdAsync("sample.party", partyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogItemDto(partyId, "Ava Allen", new RecordPayload(), false, false));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            AddReportReads(services, reports.Object);
            services.AddScoped(_ => catalogs.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What is this customer's outstanding balance as of October 21, 2026? Briefly explain what the amount represents.")],
            new("catalog", "sample.party", partyId)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(answer.Replace(",", "", StringComparison.Ordinal).Contains("6186", StringComparison.Ordinal), trace);
        Assert.DoesNotContain("two months", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2 months", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("monthly", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("installment", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lease #382", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("USD", answer);
        Assert.DoesNotContain('$', answer);
        reports.Verify(value => value.ExecuteAsync(definition.ReportCode, It.Is<ReportExecutionRequestDto>(request =>
            request.Parameters != null && request.Parameters["as_of_utc"] == "2026-10-21"
            && request.Filters != null && request.Filters["party_id"].Value.GetGuid() == partyId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaDoesNotAskAVendorForAnUnrelatedLeaseOrAccount()
    {
        var partyId = Guid.NewGuid();
        var definitions = new[]
        {
            new ReportDefinitionDto("sample.account_card", "Account Card", Description: "Account activity and running balance.",
                Filters: [new("account_id", "Account", "uuid", IsRequired: true)]),
            new ReportDefinitionDto("sample.receivables", "Receivables", Description: "Outstanding receivables for one lease.",
                Filters: [new("lease_id", "Lease", "uuid", IsRequired: true)])
        };
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(definitions);
        foreach (var definition in definitions)
        {
            reports.Setup(value => value.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>()))
                .ReturnsAsync(definition);
        }
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetTypeMetadataAsync("sample.party", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTypeMetadataDto("sample.party", "Party", EntityKind.Catalog));
        catalogs.Setup(value => value.GetByIdAsync("sample.party", partyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogItemDto(partyId, "Atlantic Builders", new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["is_vendor"] = JsonSerializer.SerializeToElement(true),
                ["is_tenant"] = JsonSerializer.SerializeToElement(false)
            }), false, false));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            services.AddScoped(_ => reports.Object);
            services.AddScoped(_ => catalogs.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What is this vendor's outstanding balance as of today?")], new("catalog", "sample.party", partyId)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        output.WriteLine(trace);
        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.DoesNotContain('?', answer);
        Assert.DoesNotContain("account_id", answer);
        Assert.DoesNotContain("lease_id", answer);
        Assert.DoesNotContain("is_vendor", answer);
        Assert.DoesNotMatch(@"(?i)\b(?:please\s+provide|(?:can|could|would)\s+you\s+provide|you\s+(?:need|have)\s+to\s+provide)\b", answer);
        Assert.DoesNotContain("0.00", answer);
        Assert.True(answer.Length < 500, trace);
        var requestedCalls = host.State.ModelInputs
            .Select(input => JsonSerializer.Deserialize<JsonElement>(input).GetProperty("response").Deserialize<ChatResponse>()!)
            .SelectMany(response => response.Messages)
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .ToArray();
        Assert.DoesNotContain(requestedCalls, call => call.Name is McpToolNames.ExecuteReport or McpToolNames.Search);
        Assert.Single(requestedCalls, call => call.Name == McpToolNames.ListReports);
        reports.Verify(value => value.ExecuteAsync(It.IsAny<string>(), It.IsAny<ReportExecutionRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Never, trace);
    }
}
