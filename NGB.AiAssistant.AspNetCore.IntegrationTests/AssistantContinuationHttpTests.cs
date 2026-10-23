using System.Net.Http.Json;
using System.Runtime.CompilerServices;
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
    [NativeOllamaFact]
    public async Task NativeOllamaContinuesTheSelectedReportWithoutLosingItsCriteria()
    {
        const string code = "sample.activity";
        var cursor = new string('c', 5000);
        var definition = new ReportDefinitionDto(code, "Activity",
            Description: "Activity entries for the selected account and date.",
            Parameters: [new("account", "string", true), new("date", "date", true)]);
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([definition]);
        reports.Setup(value => value.GetDefinitionAsync(code, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        reports.Setup(value => value.ExecuteAsync(code, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ReportExecutionRequestDto request, CancellationToken _) =>
            {
                Assert.Equal("AC-0042", request.Parameters!["account"]);
                Assert.Equal("2026-10-20", request.Parameters["date"]);
                Assert.True(request.Cursor is null || request.Cursor == cursor);
                var labels = request.Cursor is null ? new[] { "Entry Alba", "Entry Birch" } : ["Entry Cedar", "Entry Delta"];
                var rows = labels.Select(label => new ReportSheetRowDto(ReportRowKind.Detail, [Cell(label)])).ToArray();
                return new ReportExecutionResponseDto(new ReportSheetDto([new("entry", "Entry", "string")], rows),
                    request.Cursor is null ? 0 : 2, 2, 4, request.Cursor is null, request.Cursor is null ? cursor : null);
            });
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddReportReads(services, reports.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "List all activity entries in this report, including any entries on later pages.")],
            new("report", code, Parameters: new Dictionary<string, string> { ["account"] = "AC-0042", ["date"] = "2026-10-20" })));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = answer + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.Contains(events, item => item.Type == "done");
        foreach (var label in new[] { "Entry Alba", "Entry Birch", "Entry Cedar", "Entry Delta" })
            Assert.True(answer.Contains(label, StringComparison.Ordinal), trace);
        reports.Verify(value => value.ExecuteAsync(code, It.Is<ReportExecutionRequestDto>(request => request.Cursor == cursor),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(1048576)]
    [InlineData(40000)]
    public async Task ReportContinuationRetainsTheAuthorizedSelectionAndAdaptsOversizedPages(int responseBytes)
    {
        const string code = "sample.activity";
        var cursor = new string('x', 5000);
        var scopeId = Guid.NewGuid().ToString();
        var executions = new List<ReportExecutionRequestDto>();
        var definition = new ReportDefinitionDto(code, "Activity",
            Parameters: [new("scope", "string", true), new("date", "date", true)]);
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetDefinitionAsync(code, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        reports.Setup(value => value.ExecuteAsync(code, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ReportExecutionRequestDto request, CancellationToken _) =>
            {
                executions.Add(request);
                Assert.Equal(scopeId, request.Parameters!["scope"]);
                Assert.Equal("2026-10-20", request.Parameters["date"]);
                var rows = Enumerable.Range(0, request.Limit).Select(index => new ReportSheetRowDto(
                    ReportRowKind.Detail, [new ReportCellDto(JsonSerializer.SerializeToElement(new string('a', 1000)), null, "string")])).ToArray();
                return new ReportExecutionResponseDto(new ReportSheetDto([new("text", "Text", "string")], rows),
                    request.Cursor is null ? 0 : 5, request.Limit, 10, request.Cursor is null,
                    request.Cursor is null ? cursor : null);
            });
        var model = new WorkflowModel((round, input) =>
        {
            if (round == 0)
                return ToolCall("first", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code,
                    ["request"] = new { limit = 20, parameters = new { scope = scopeId, date = "2026-10-20" } }
                });

            var result = LatestToolData(input);
            if (round == 1)
            {
                var handle = result.GetProperty("data").GetProperty("nextCursor").GetString();
                Assert.StartsWith("page-", handle);
                Assert.DoesNotContain(cursor, result.GetRawText());
                Assert.True(result.GetProperty("data").GetProperty("hasMore").GetBoolean());
                return ToolCall("next", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code,
                    // Repeating the original limit must retain the smaller page that fit.
                    ["request"] = new { cursor = handle, limit = 20 }
                });
            }

            Assert.False(result.GetProperty("data").GetProperty("hasMore").GetBoolean());
            return Answer("Both pages were read.");
        });
        await using var host = await Host.StartAsync(overrides: new() { ["AiAssistant:MaxToolResultBytes"] = "8000" }, configure: services =>
        {
            AddReportReads(services, reports.Object);
            services.Configure<McpReadOptions>(options => options.MaxResponseBytes = responseBytes);
            services.AddSingleton<IChatClient>(model);
        });
        using var http = host.Client("alex");

        using var response = await http.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains(events, item => item.Type == "done");
        Assert.Equal([20, 10, 5, 5], executions.Select(request => request.Limit));
        Assert.All(executions.Take(3), request => Assert.Null(request.Cursor));
        Assert.Equal(cursor, executions[^1].Cursor);
    }

    [Theory]
    [InlineData("different")]
    [InlineData("unknown-cursor")]
    [InlineData("different-view")]
    public async Task ReportContinuationCannotChangeScopeOrInventACursor(string alteration)
    {
        const string code = "sample.selection";
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetDefinitionAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportDefinitionDto(code, "Selection", Parameters: [new("scope", "string", true)]));
        reports.Setup(value => value.ExecuteAsync(code, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportExecutionResponseDto(new ReportSheetDto([], []), 0, 10, 20, true, "native-cursor"));
        var model = new WorkflowModel((round, input) =>
        {
            if (round == 0)
                return ToolCall("first", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code, ["request"] = new { parameters = new { scope = "original" } }
                });
            if (round == 1)
            {
                var handle = LatestToolData(input).GetProperty("data").GetProperty("nextCursor").GetString();
                if (alteration == "different-view")
                    return ToolCall("invalid", McpToolNames.ExecuteReport, new()
                    {
                        ["reportCode"] = code,
                        ["resultView"] = "totals",
                        ["request"] = new { cursor = handle }
                    });

                return ToolCall("invalid", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code,
                    ["request"] = new { cursor = alteration == "unknown-cursor" ? alteration : handle, parameters = new { scope = alteration } }
                });
            }

            Assert.Contains("invalid_cursor", LatestToolData(input).GetRawText());
            return Answer("The selected page could not be continued.");
        });
        await using var host = await Host.StartAsync(configure: services =>
        {
            AddReportReads(services, reports.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var http = host.Client("alex");

        using var response = await http.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.Contains(events, item => item.Type == "done");
        reports.Verify(value => value.ExecuteAsync(code, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OversizedContinuationReducesTheRetainedPageSizeWhenTheModelOmitsLimit()
    {
        const string code = "sample.activity";
        const string cursor = "native-continuation";
        var executions = new List<ReportExecutionRequestDto>();
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetDefinitionAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportDefinitionDto(code, "Activity", Parameters: [new("scope", "string", true)]));
        reports.Setup(value => value.ExecuteAsync(code, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ReportExecutionRequestDto request, CancellationToken _) =>
            {
                executions.Add(request);
                Assert.Equal("original", request.Parameters!["scope"]);
                var text = request.Cursor is null ? "Small entry" : new string('a', 2000);
                var rows = Enumerable.Range(0, request.Limit).Select(_ => new ReportSheetRowDto(
                    ReportRowKind.Detail, [new ReportCellDto(JsonSerializer.SerializeToElement(text), null, "string")])).ToArray();
                return new ReportExecutionResponseDto(new ReportSheetDto([new("entry", "Entry", "string")], rows),
                    request.Cursor is null ? 0 : 4, request.Limit, 8, request.Cursor is null || request.Limit < 4,
                    request.Cursor is null ? cursor : request.Limit < 4 ? "later-page" : null);
            });
        var model = new WorkflowModel((round, input) =>
        {
            if (round == 0)
                return ToolCall("first", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code, ["request"] = new { limit = 4, parameters = new { scope = "original" } }
                });
            if (round == 1)
            {
                var handle = LatestToolData(input).GetProperty("data").GetProperty("nextCursor").GetString();
                return ToolCall("next", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code, ["request"] = new { cursor = handle }
                });
            }

            var result = LatestToolData(input).GetProperty("data");
            Assert.True(result.GetProperty("hasMore").GetBoolean());
            Assert.Equal(2, result.GetProperty("rows").GetArrayLength());
            return Answer("Six entries were read; more remain.");
        });
        await using var host = await Host.StartAsync(overrides: new() { ["AiAssistant:MaxToolResultBytes"] = "8000" }, configure: services =>
        {
            AddReportReads(services, reports.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var http = host.Client("alex");

        using var response = await http.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains(events, item => item.Type == "done");
        Assert.Equal([4, 4, 2], executions.Select(request => request.Limit));
        Assert.All(executions.Skip(1), request => Assert.Equal(cursor, request.Cursor));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task RecordContinuationDistinguishesMissingFiltersFromAnUnfilteredScan(bool filtered, bool changeSearch)
    {
        const string type = "sample.agreement";
        const string cursor = "next-record-page";
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(type, "Agreements", EntityKind.Document,
                List: new ListMetadataDto([], [new("group", "Group", DataType.String)])));
        documents.Setup(value => value.GetPageAsync(type, It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, PageRequestDto request, CancellationToken _) =>
            {
                if (filtered)
                    Assert.Equal("West", request.Filters!["group"]);
                else
                    Assert.Null(request.Cursor);

                return new PageResponseDto<DocumentDto>([], 0, 10, null, request.Cursor is null,
                    request.Cursor is null ? cursor : null);
            });
        var model = new WorkflowModel((round, input) =>
        {
            var arguments = new Dictionary<string, object?> { ["kind"] = "document", ["typeCode"] = type };
            if (round == 0)
                return ToolCall("metadata", McpToolNames.GetMetadata, arguments);
            if (round == 1)
            {
                if (filtered)
                    arguments["filters"] = new Dictionary<string, string> { ["group"] = "West" };
                return ToolCall("first", McpToolNames.ListRecords, arguments);
            }
            if (round == 2)
            {
                arguments["cursor"] = cursor;
                if (changeSearch)
                    arguments["search"] = "Agreements";
                return ToolCall("next", McpToolNames.ListRecords, arguments);
            }
            if (!filtered)
            {
                Assert.DoesNotContain(input, message => message.Role == ChatRole.Tool);
                Assert.Contains("unfiltered_scan", input[^1].Text);
                return Answer("The requested selection cannot be determined.");
            }
            if (round == 3)
            {
                Assert.Contains("filter_required", LatestToolData(input).GetRawText());
                arguments["cursor"] = cursor;
                arguments["filters"] = new Dictionary<string, string> { ["group"] = "West" };
                return ToolCall("corrected", McpToolNames.ListRecords, arguments);
            }

            Assert.False(LatestToolData(input).GetProperty("data").GetProperty("documents").GetProperty("hasMore").GetBoolean());
            return Answer("The selected pages were read.");
        });
        await using var host = await Host.StartAsync(configure: services =>
        {
            services.AddScoped(_ => documents.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var http = host.Client("alex");

        using var response = await http.PostAsJsonAsync("/api/ai-assistant/chat", Question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains(events, item => item.Type == "done");
        documents.Verify(value => value.GetPageAsync(type, It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(filtered ? 2 : 1));
    }

    private static JsonElement LatestToolData(ChatMessage[] input)
        => JsonSerializer.SerializeToElement(input.Last(message => message.Role == ChatRole.Tool)
            .Contents.OfType<FunctionResultContent>().Single().Result);

    private static ChatResponseUpdate ToolCall(string id, string name, Dictionary<string, object?> arguments)
        => new(ChatRole.Assistant, [new FunctionCallContent(id, name, arguments)]) { FinishReason = ChatFinishReason.ToolCalls };

    private static ChatResponseUpdate Answer(string text)
        => new(ChatRole.Assistant, text) { FinishReason = ChatFinishReason.Stop };

    private sealed class WorkflowModel(Func<int, ChatMessage[], ChatResponseUpdate> next, int? requiredAnswerTokens = null) : IChatClient
    {
        private int _round;
        private ChatResponseUpdate? _pendingAnswer;

        internal List<ChatOptions> Options { get; } = [];
        internal List<ChatMessage[]> Requests { get; } = [];
        internal int TruncatedAnswers { get; private set; }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Options.Add(options!.Clone());
            var input = messages.ToArray();
            Requests.Add(input);
            var response = _pendingAnswer ?? next(_round++, input);
            if (response.FinishReason == ChatFinishReason.Stop
                && requiredAnswerTokens is { } required
                && options.MaxOutputTokens < required)
            {
                // Emulate the provider's output ceiling; this is not a tokenizer.
                // A recovery must preserve evidence and increase the supplied budget.
                _pendingAnswer = response;
                TruncatedAnswers++;
                yield return new(ChatRole.Assistant, "Truncated report draft") { FinishReason = ChatFinishReason.Length };
            }
            else
            {
                yield return response;
            }

            await Task.CompletedTask;
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
