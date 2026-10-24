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
    public async Task RecordLookupDeclarationsReachTheModelWithoutInventingTargets(bool declaredLookup)
    {
        const string type = "sample.record";
        var id = Guid.NewGuid();
        var relatedId = Guid.NewGuid();
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        var metadata = new DocumentTypeMetadataDto(type, "Record", EntityKind.Document,
            Form: declaredLookup ? new FormMetadataDto([new("Main", [new([
                new("related_record", "Agreement", DataType.Guid, UiControl.Lookup,
                    Lookup: new DocumentLookupSourceDto(["sample.agreement"]))
            ])])]) : null);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>())).ReturnsAsync(metadata);
        documents.Setup(value => value.GetByIdAsync(type, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(id, "Record R-0042", new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["related_record"] = JsonSerializer.SerializeToElement(relatedId)
            }), DocumentStatus.Posted, false));
        var model = new WorkflowModel((round, input) =>
        {
            if (round == 0)
                return ToolCall("record", McpToolNames.GetRecord, new()
                {
                    ["kind"] = "document", ["typeCode"] = type, ["id"] = id
                });

            var result = LatestToolData(input).GetProperty("data");
            Assert.Equal("Record", result.GetProperty("typeName").GetString());
            var fields = result.GetProperty("lookupFields");
            if (declaredLookup)
            {
                var field = Assert.Single(fields.EnumerateArray());
                Assert.Equal("related_record", field.GetProperty("key").GetString());
                Assert.Equal("document", field.GetProperty("lookup").GetProperty("kind").GetString());
                Assert.Equal("sample.agreement", Assert.Single(field.GetProperty("lookup")
                    .GetProperty("documentTypes").EnumerateArray()).GetString());
            }
            else
            {
                Assert.Empty(fields.EnumerateArray());
            }

            return Answer("The current record was read.");
        });
        await using var host = await Host.StartAsync(configure: services =>
        {
            AddInvoiceReads(services, documents.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains(events, item => item.Type == "done");
        Assert.DoesNotContain(events, item => item.Source?.Id == relatedId);
        documents.Verify(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()), Times.Once);
        documents.Verify(value => value.GetByIdAsync(type, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Amount: $**227.5006**.", "Amount: 227.5006.", 227.5006, 3)]
    [InlineData("Amount: −227.5006.", "Amount: 227.5006.", 227.5006, 3)]
    [InlineData("Received: {{ngb-value:earlier-turn-0}}.", "Received: 2025-08-12.", 227.5006, 3)]
    [InlineData("Amount: 1 457.00.", "Not needed.", 1457, 2)]
    public async Task AnswerEvidenceChecksSurviveTheHttpAndMcpTransport(
        string draft, string correction, decimal amount, int expectedRounds)
    {
        const string type = "sample.payment";
        var id = Guid.NewGuid();
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(type, "Payment", EntityKind.Document));
        documents.Setup(value => value.GetByIdAsync(type, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(id, "Payment PAY-0042", new RecordPayload(
                new Dictionary<string, JsonElement>
                {
                    ["amount"] = JsonSerializer.SerializeToElement(amount),
                    ["received_on"] = JsonSerializer.SerializeToElement("2025-08-12")
                }), DocumentStatus.Posted, false));
        var rounds = 0;
        var model = new WorkflowModel((round, _) =>
        {
            rounds++;
            return round switch
            {
                0 => ToolCall("payment", McpToolNames.GetRecord, new()
                {
                    ["kind"] = "document", ["typeCode"] = type, ["id"] = id
                }),
                1 => Answer(draft),
                _ => Answer(correction)
            };
        });
        await using var host = await Host.StartAsync(configure: services =>
        {
            AddInvoiceReads(services, documents.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What are this payment's amount and received date?")], new("document", type, id)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var date = Assert.Single(events, item => item.Value?.Value == "2025-08-12").Value!;
        var expected = expectedRounds == 2 ? draft : correction.Replace("2025-08-12", date.Reference, StringComparison.Ordinal);

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains(events, item => item.Type == "done");
        Assert.Equal(expected, answer);
        Assert.Equal(expectedRounds, rounds);
        documents.Verify(value => value.GetByIdAsync(type, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(100)]
    [InlineData(200)]
    public async Task ARequestedReportPageIsNotSplitWhenItsResultFitsTheBudget(int limit)
    {
        const string code = "sample.activity";
        const int total = 30;
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetDefinitionAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportDefinitionDto(code, "Activity", Parameters: [new("scope", "string", true)]));
        reports.Setup(value => value.ExecuteAsync(code, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ReportExecutionRequestDto request, CancellationToken _) =>
            {
                var rows = Enumerable.Range(1, Math.Min(total, request.Limit))
                    .Select(index => new ReportSheetRowDto(ReportRowKind.Detail, [Cell($"Entry {index}")]))
                    .ToArray();
                return new ReportExecutionResponseDto(new ReportSheetDto([new("entry", "Entry", "string")], rows),
                    0, request.Limit, total, rows.Length < total, rows.Length < total ? "next-page" : null);
            });
        var rounds = 0;
        var model = new WorkflowModel((round, input) =>
        {
            rounds++;
            if (round == 0)
                return ToolCall("report", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code,
                    ["request"] = new { limit, parameters = new { scope = "requested" } }
                });

            var result = LatestToolData(input).GetProperty("data");
            Assert.False(result.GetProperty("hasMore").GetBoolean());
            Assert.Equal(total, result.GetProperty("rows").GetArrayLength());
            return Answer("The complete requested page was read.");
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
        Assert.Equal(2, rounds);
        reports.Verify(value => value.ExecuteAsync(code, It.Is<ReportExecutionRequestDto>(request =>
            request.Limit == limit && request.Cursor == null && request.Parameters!["scope"] == "requested"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(100)]
    public async Task LargerReportPagesStillReduceToTheEvidenceBudgetWithoutChangingSelection(int limit)
    {
        const string code = "sample.activity";
        var executions = new List<ReportExecutionRequestDto>();
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetDefinitionAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportDefinitionDto(code, "Activity", Parameters: [new("scope", "string", true)]));
        reports.Setup(value => value.ExecuteAsync(code, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ReportExecutionRequestDto request, CancellationToken _) =>
            {
                executions.Add(request);
                var rows = Enumerable.Range(1, request.Limit)
                    .Select(_ => new ReportSheetRowDto(ReportRowKind.Detail,
                        [new(JsonSerializer.SerializeToElement(new string('a', 1000)), null, "string")]))
                    .ToArray();
                return new ReportExecutionResponseDto(new ReportSheetDto([new("entry", "Entry", "string")], rows),
                    0, request.Limit, 500, true, "next-page");
            });
        var rounds = 0;
        var model = new WorkflowModel((round, input) =>
        {
            rounds++;
            if (round == 0)
                return ToolCall("report", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code,
                    ["request"] = new { limit, parameters = new { scope = "requested" } }
                });

            var result = LatestToolData(input);
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(result).Length <= 8000);
            var data = result.GetProperty("data");
            Assert.True(data.GetProperty("hasMore").GetBoolean());
            Assert.Equal(executions[^1].Limit, data.GetProperty("rows").GetArrayLength());
            Assert.StartsWith("page-", data.GetProperty("nextCursor").GetString());
            return Answer("Only part of the activity has been read; further entries remain.");
        });
        await using var host = await Host.StartAsync(overrides: new() { ["AiAssistant:MaxToolResultBytes"] = "8000" }, configure: services =>
        {
            AddReportReads(services, reports.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains(events, item => item.Type == "done");
        Assert.Equal(2, rounds);
        Assert.Equal(limit, executions[0].Limit);
        Assert.InRange(executions[^1].Limit, 1, limit - 1);
        Assert.All(executions, request =>
        {
            Assert.Equal("requested", request.Parameters!["scope"]);
            Assert.Null(request.Cursor);
        });
        Assert.All(executions.Zip(executions.Skip(1)), pair => Assert.True(pair.Second.Limit < pair.First.Limit));
    }
}
