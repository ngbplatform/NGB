using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Mcp;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed partial class AssistantHttpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PortfolioTotalOnTheLastPageFitsTheDefaultAssistantBudgets(bool hasTotal)
    {
        const string code = "sample.occupancy";
        const int buildingCount = 601;
        const int vacantUnits = 123456;
        var definition = new ReportDefinitionDto(code, "Occupancy Summary",
            Parameters: [new("as_of_utc", "date", true)],
            Filters: [new("building_id", "Building", "uuid", Lookup: new CatalogLookupSourceDto("sample.building"))]);
        var columns = new ReportSheetColumnDto[]
        {
            new("building", "Building", "string"),
            new("as_of_utc", "As Of", "date"),
            new("total_units", "Total Units", "int32"),
            new("occupied_units", "Occupied Units", "int32"),
            new("vacant_units", "Vacant Units", "int32"),
            new("occupancy_percent", "Occupancy %", "decimal")
        };
        var executions = new List<ReportExecutionRequestDto>();
        var reports = PagedReportReads(definition);
        reports.Setup(value => value.ExecuteAsync(code, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ReportExecutionRequestDto request, CancellationToken _) =>
            {
                executions.Add(request);
                Assert.Equal("2026-10-20", request.Parameters!["as_of_utc"]);
                Assert.True(request.Filters is null || request.Filters.Count == 0);
                Assert.False(request.DisablePaging);
                Assert.Null(request.Layout);
                var offset = request.Cursor is null ? 0 : int.Parse(request.Cursor, CultureInfo.InvariantCulture);
                var count = Math.Min(request.Limit - 1, buildingCount - offset);
                var rows = Enumerable.Range(offset, count).Select(index => new ReportSheetRowDto(ReportRowKind.Detail,
                [
                    ReportValue($"Building {index}, Dataset performance, Hoboken, NJ 07030", "string"),
                    ReportValue("2026-10-20", "date"),
                    ReportValue(500, "int32"),
                    ReportValue(169, "int32"),
                    ReportValue(331, "int32"),
                    ReportValue(33.8m, "decimal")
                ])).ToList();
                var hasMore = offset + count < buildingCount;
                if (!hasMore && hasTotal)
                {
                    rows.Add(new(ReportRowKind.Total,
                    [
                        ReportValue("Total", "string"),
                        ReportValue("2026-10-20", "date"),
                        ReportValue(300500, "int32"),
                        ReportValue(177044, "int32"),
                        ReportValue(vacantUnits, "int32"),
                        ReportValue(58.9165m, "decimal")
                    ], SemanticRole: "grand_total"));
                }

                return new ReportExecutionResponseDto(new(columns, rows, new("Occupancy Summary")),
                    offset, count, hasMore ? null : buildingCount, hasMore,
                    hasMore ? (offset + count).ToString(CultureInfo.InvariantCulture) : null);
            });
        var model = new WorkflowModel((round, input) =>
        {
            if (round == 0)
                return ToolCall("reports", McpToolNames.ListReports, new());
            if (round == 1)
                return ToolCall("definition", McpToolNames.GetReportDefinition, new() { ["reportCode"] = code });
            if (round == 2)
                return ToolCall("first", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code,
                    ["resultView"] = "totals",
                    ["request"] = new { parameters = new { as_of_utc = "2026-10-20" } }
                });

            var data = LatestToolData(input).GetProperty("data");
            Assert.Equal("totals", data.GetProperty("resultView").GetString());
            Assert.True(data.GetProperty("omittedRows").GetInt32() > 0);
            var rows = data.GetProperty("rows");
            if (data.GetProperty("hasMore").GetBoolean())
            {
                Assert.Equal(0, rows.GetArrayLength());
                return ToolCall($"page-{round}", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code,
                    ["request"] = new { cursor = data.GetProperty("nextCursor").GetString() }
                });
            }

            if (!hasTotal)
            {
                Assert.Equal(0, rows.GetArrayLength());
                return Answer("The report returned no portfolio total.");
            }

            Assert.Equal("total", rows[0].GetProperty("rowKind").GetString());
            Assert.Equal("grand_total", rows[0].GetProperty("semanticRole").GetString());
            var vacant = rows[0].GetProperty("cells")[4].GetProperty("value").GetInt32();
            Assert.Equal(vacantUnits, vacant);
            return Answer($"{vacant} units were vacant.");
        });
        await using var host = await Host.StartAsync(configure: services =>
        {
            AddReportReads(services, reports.Object);
            services.AddSingleton<IChatClient>(model);
        });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "How many units were vacant across all buildings as of October 20, 2026?")]));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains(events, item => item.Type == "done");
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        Assert.Contains(hasTotal ? "123456 units" : "no portfolio total", answer);
        Assert.Equal(4, executions.Count);
        Assert.All(executions, request => Assert.Equal(200, request.Limit));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(12, true)]
    [InlineData(null, false)]
    public async Task AllAgingPagesRetainTheirBucketsAndExactAmountsWithBoundedOutputRecovery(int? pageSize, bool allowLongAnswer)
    {
        const string code = "sample.aging";
        const int chargeCount = 40;
        var lease = Guid.NewGuid();
        var definition = new ReportDefinitionDto(code, "Receivables Aging",
            Parameters: [new("as_of_utc", "date", true)],
            Filters: [new("lease_id", "Lease", "uuid", IsRequired: true, Lookup: new DocumentLookupSourceDto(["sample.lease"]))]);
        var columns = new ReportSheetColumnDto[]
        {
            new("bucket", "Bucket", "string"),
            new("charge", "Charge", "string"),
            new("charge_type", "Charge Type", "string"),
            new("due_on_utc", "Due On", "date"),
            new("days_past_due", "Days Past Due", "int32"),
            new("original_amount", "Original", "decimal"),
            new("outstanding_amount", "Outstanding", "decimal")
        };
        var executions = new List<ReportExecutionRequestDto>();
        var reports = PagedReportReads(definition);
        reports.Setup(value => value.ExecuteAsync(code, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ReportExecutionRequestDto request, CancellationToken _) =>
            {
                executions.Add(request);
                Assert.Equal("2026-10-20", request.Parameters!["as_of_utc"]);
                Assert.Equal(lease, request.Filters!["lease_id"].Value.GetGuid());
                Assert.False(request.DisablePaging);
                Assert.Null(request.Layout);
                var offset = request.Cursor is null ? 0 : int.Parse(request.Cursor, CultureInfo.InvariantCulture);
                var count = Math.Min(request.Limit - 1, chargeCount - offset);
                var rows = Enumerable.Range(offset, count).Select(index => new ReportSheetRowDto(ReportRowKind.Detail,
                [
                    ReportValue(index < 20 ? "Past due 91+ days" : "Past due 1–30 days", "string"),
                    ReportValue($"RC-2026-{index + 1:D6}", "string"),
                    ReportValue("Rent Charge", "string"),
                    ReportValue(index < 20 ? "2026-01-01" : "2026-10-01", "date"),
                    ReportValue(index < 20 ? 292 : 19, "int32"),
                    ReportValue(3115.1234m, "decimal") with { Display = "3115.12" },
                    ReportValue(1937.5678m, "decimal") with { Display = "1937.57" }
                ])).ToList();
                rows.Add(new(ReportRowKind.Total,
                [
                    ReportValue("Total", "string"),
                    ReportValue("", "string"),
                    ReportValue("", "string"),
                    ReportValue("", "string"),
                    ReportValue("", "string"),
                    ReportValue(124604.9360m, "decimal"),
                    ReportValue(77502.7120m, "decimal")
                ], SemanticRole: "grand_total"));
                var hasMore = offset + count < chargeCount;
                return new ReportExecutionResponseDto(new(columns, rows, new("Aging", "Selected lease")),
                    offset, count, chargeCount, hasMore,
                    hasMore ? (offset + count).ToString(CultureInfo.InvariantCulture) : null);
            });
        var model = new WorkflowModel((round, input) =>
        {
            if (round == 0)
                return ToolCall("reports", McpToolNames.ListReports, new());
            if (round == 1)
                return ToolCall("definition", McpToolNames.GetReportDefinition, new() { ["reportCode"] = code });
            if (round == 2)
                return ToolCall("first", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code,
                    ["request"] = new
                    {
                        parameters = new { as_of_utc = "2026-10-20" },
                        filters = new { lease_id = new { value = lease } }
                    }
                });

            var data = LatestToolData(input).GetProperty("data");
            if (data.GetProperty("hasMore").GetBoolean())
                return ToolCall($"page-{round}", McpToolNames.ExecuteReport, new()
                {
                    ["reportCode"] = code,
                    ["request"] = new { cursor = data.GetProperty("nextCursor").GetString() }
                });

            var details = input.Where(message => message.Role == ChatRole.Tool)
                .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
                .Select(result => JsonSerializer.SerializeToElement(result.Result).GetProperty("data"))
                .Where(result => result.TryGetProperty("rows", out _))
                .SelectMany(result => result.GetProperty("rows").EnumerateArray())
                .Where(row => row.GetProperty("rowKind").GetString() == "detail")
                .ToArray();
            Assert.Equal(chargeCount, details.Length);
            var answer = new List<string> { "| Charge | Bucket | Outstanding |", "|---|---|---|" };
            for (var index = 0; index < details.Length; index++)
            {
                var cells = details[index].GetProperty("cells");
                Assert.Equal($"RC-2026-{index + 1:D6}", cells[1].GetProperty("value").GetString());
                Assert.Equal(1937.5678m, cells[6].GetProperty("value").GetDecimal());
                Assert.Equal("1937.57", cells[6].GetProperty("display").GetString());
                answer.Add($"| {cells[1].GetProperty("value").GetString()} | {cells[0].GetProperty("value").GetString()} | 1937.5678 |");
            }

            return Answer(string.Join('\n', answer));
        }, requiredAnswerTokens: 3000);
        var limits = new AiAssistantOptions();
        if (!allowLongAnswer)
            limits.MaxOutputTokens = limits.InitialOutputTokens;
        await using var host = await Host.StartAsync(configure: services =>
        {
            AddReportReads(services, reports.Object);
            services.AddSingleton<IChatClient>(model);
            services.Configure<AiAssistantOptions>(options => options.MaxOutputTokens = limits.MaxOutputTokens);
            if (pageSize is { } configuredPageSize)
                services.Configure<AiAssistantHttpOptions>(options => options.ReportPageSize = configuredPageSize);
        });
        using var client = host.Client("alex");

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Show the receivables aging buckets for this lease as of October 20, 2026.")],
            new("document", "sample.lease", lease)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));

        var expectedPageSize = pageSize ?? new AiAssistantHttpOptions().ReportPageSize;
        Assert.Equal((int)Math.Ceiling((double)chargeCount / (expectedPageSize - 1)), executions.Count);
        Assert.All(executions, request => Assert.Equal(expectedPageSize, request.Limit));
        Assert.Equal(1, model.TruncatedAnswers);
        Assert.All(model.Options, options => Assert.True(options.MaxOutputTokens <= limits.MaxOutputTokens));
        Assert.DoesNotContain("Truncated report draft", answer);

        if (allowLongAnswer)
        {
            Assert.DoesNotContain(events, item => item.Type == "error");
            Assert.Contains(events, item => item.Type == "done");
            var retainedEvidence = string.Join('\n', model.Requests[^1].Select(message => message.Text));
            for (var index = 1; index <= chargeCount; index++)
            {
                Assert.Contains($"RC-2026-{index:D6}", answer);
                Assert.Contains($"RC-2026-{index:D6}", retainedEvidence);
            }

            Assert.DoesNotContain("Truncated report draft", retainedEvidence);
            Assert.Equal(limits.MaxOutputTokens, model.Options[^1].MaxOutputTokens);
            Assert.Empty(model.Options[^1].Tools!);
        }
        else
        {
            Assert.Equal("response_limit", Assert.Single(events, item => item.Type == "error").Code);
            Assert.DoesNotContain(events, item => item.Type is "text" or "done");
        }
    }

    private static Mock<IAuthorizedReportReadService> PagedReportReads(ReportDefinitionDto definition)
    {
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        var definitions = Enumerable.Range(1, 6).Select(index => new ReportDefinitionDto(
            $"sample.other_{index}", $"Other report {index}", Description: new string('d', 500)))
            .Prepend(definition).ToArray();
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(definitions);
        reports.Setup(value => value.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>())).ReturnsAsync(definition);

        return reports;
    }

    private static ReportCellDto ReportValue<T>(T value, string type)
        => new(JsonSerializer.SerializeToElement(value), Convert.ToString(value, CultureInfo.InvariantCulture), type);
}
