using System.Collections.Concurrent;
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
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed partial class AssistantHttpTests
{
    [NativeOllamaFact]
    public async Task NativeOllamaAnswersVacancyFromAnExecutedReportInsteadOfSearchHits()
    {
        foreach (var context in new AiAssistantPageContext?[]
                 {
                     new("report", VacancyReportScenario.ReportCode),
                     new("catalog", VacancyReportScenario.PropertyType),
                     null
                 })
        {
            await AssertNativeVacancyAnswerAsync(context);
        }
    }

    private static async Task AssertNativeVacancyAnswerAsync(AiAssistantPageContext? context)
    {
        var scenario = new VacancyReportScenario();
        await using var host = await Host.StartAsync(mode: "ollama-report", configure: scenario.Configure);
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);
        var question = new AiAssistantRequest(
            [new("user", "How many vacant units in 100 Hudson Ave Dataset perf-20260510b?")],
            context);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", question);
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        var trace = "Context: " + JsonSerializer.Serialize(context) + "\n" + string.Join('\n', host.State.ModelInputs);

        Assert.True(events.All(item => item.Type != "error"), trace);
        Assert.True(scenario.Executions.Count > 0, trace);
        Assert.True(scenario.Executions.All(request => request.Parameters is null || request.Parameters.Count == 0), trace);
        Assert.Contains(scenario.VacantUnits.ToString(), answer);
        Assert.DoesNotContain("building_id", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("as_of_utc", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UUID", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("would you like", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(events, item => item.Type == "done");
    }

    [Fact]
    public async Task ReportFilterShapeAndOptionalParametersSurviveTheMcpTransport()
    {
        var scenario = new VacancyReportScenario();
        await using var host = await Host.StartAsync(configure: scenario.Configure);
        using var http = host.Client("alex");
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp")
        }, http));

        var metadata = await client.CallToolAsync(McpToolNames.GetReportDefinition, new Dictionary<string, object?>
        {
            ["reportCode"] = VacancyReportScenario.ReportCode
        });
        Assert.NotEqual(true, metadata.IsError);
        var definition = metadata.StructuredContent!.Value.GetProperty("data");
        Assert.False(definition.GetProperty("parameters")[0].GetProperty("isRequired").GetBoolean());
        Assert.True(definition.GetProperty("filters")[0].GetProperty("isRequired").GetBoolean());

        var arguments = new Dictionary<string, object?>
        {
            ["reportCode"] = VacancyReportScenario.ReportCode,
            ["request"] = new { filters = new { building_id = scenario.BuildingId } }
        };
        var invalid = await client.CallToolAsync(McpToolNames.ExecuteReport, arguments);
        Assert.True(invalid.IsError);
        Assert.Empty(scenario.Executions);

        arguments["request"] = new { filters = new { building_id = new { value = scenario.BuildingId } } };
        var valid = await client.CallToolAsync(McpToolNames.ExecuteReport, arguments);
        Assert.NotEqual(true, valid.IsError);
        var execution = Assert.Single(scenario.Executions);
        Assert.Null(execution.Parameters);
        Assert.Equal(scenario.BuildingId, execution.Filters!["building_id"].Value.GetGuid());
        var cells = valid.StructuredContent!.Value.GetProperty("data").GetProperty("sheet")
            .GetProperty("rows")[0].GetProperty("cells");
        Assert.Equal(scenario.VacantUnits, cells[3].GetProperty("value").GetInt32());
    }

    private sealed class VacancyReportScenario
    {
        internal const string ReportCode = "pm.building.summary";
        internal const string PropertyType = "pm.property";
        internal const string BuildingDisplay = "100 Hudson Ave Dataset perf-20260510b, Hoboken, NJ 07030";

        internal Guid BuildingId { get; } = Guid.NewGuid();
        internal int VacantUnits { get; } = Random.Shared.Next(250, 400);
        internal ConcurrentQueue<ReportExecutionRequestDto> Executions { get; } = new();

        internal void Configure(IServiceCollection services)
        {
            var definition = new ReportDefinitionDto(
                ReportCode,
                "Building Summary",
                Description: "Canonical PM building summary",
                Parameters: [new("as_of_utc", "date", false, Label: "As of")],
                Filters: [new("building_id", "Building", "uuid", IsRequired: true,
                    Lookup: new CatalogLookupSourceDto(PropertyType))]);
            var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
            reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([definition]);
            reports.Setup(value => value.GetDefinitionAsync(ReportCode, It.IsAny<CancellationToken>()))
                .ReturnsAsync(definition);
            reports.Setup(value => value.ExecuteAsync(ReportCode, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, ReportExecutionRequestDto request, CancellationToken _) =>
                {
                    var building = CanonicalReportExecutionHelper.GetRequiredGuidFilter(definition, request, "building_id");
                    if (building != BuildingId)
                        throw new McpReadException("invalid_input", "Select the building returned by search.");

                    Executions.Enqueue(request);
                    return Result();
                });

            var search = new Mock<INgbSearchService>(MockBehavior.Strict);
            search.Setup(value => value.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<NgbSearchKind?>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, int limit, NgbSearchKind? kind, string? typeCode, CancellationToken _) =>
                {
                    var hits = new List<NgbSearchHit>
                    {
                        new("report", ReportCode, null, "Building Summary"),
                        new("catalog", PropertyType, BuildingId, BuildingDisplay)
                    };
                    hits.AddRange(Enumerable.Range(101, 5).Select(number =>
                        new NgbSearchHit("catalog", "pm.unit", Guid.NewGuid(), $"{BuildingDisplay} #{number}")));
                    var filtered = hits.Where(hit => (kind is null || hit.Kind.Equals(kind.ToString(), StringComparison.OrdinalIgnoreCase))
                        && (typeCode is null || hit.TypeCode == typeCode)).Take(limit).ToArray();

                    return new NgbSearchResult(filtered, true, []);
                });
            var catalogMetadata = new CatalogTypeMetadataDto(PropertyType, "Properties", EntityKind.Catalog);
            var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
            catalogs.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([catalogMetadata]);
            catalogs.Setup(value => value.GetTypeMetadataAsync(PropertyType, It.IsAny<CancellationToken>()))
                .ReturnsAsync(catalogMetadata);
            catalogs.Setup(value => value.GetByIdAsync(PropertyType, BuildingId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CatalogItemDto(BuildingId, BuildingDisplay, new RecordPayload(), false, false));
            var scopes = new Mock<IRecordReadScopeFactory>(MockBehavior.Strict);
            scopes.Setup(value => value.OpenAsync(
                    It.IsAny<BusinessObjectRef>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<IAsyncDisposable>());
            var budget = new Mock<IReportRequestAdmission>();
            budget.SetupGet(value => value.ExecutionTimeout).Returns(TimeSpan.FromSeconds(30));
            budget.Setup(value => value.AcquireAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<IDisposable>());
            services.AddScoped(_ => reports.Object);
            services.AddScoped(_ => search.Object);
            services.AddScoped(_ => catalogs.Object);
            services.AddScoped(_ => scopes.Object);
            services.AddScoped(_ => budget.Object);
        }

        private ReportExecutionResponseDto Result()
        {
            var columns = new ReportSheetColumnDto[]
            {
                new("building", "Building", "string"),
                new("total_units", "Total Units", "int32"),
                new("occupied_units", "Occupied Units", "int32"),
                new("vacant_units", "Vacant Units", "int32")
            };
            var cells = new ReportCellDto[]
            {
                new(JsonSerializer.SerializeToElement(BuildingDisplay), BuildingDisplay, "string"),
                new(JsonSerializer.SerializeToElement(500), "500", "int32"),
                new(JsonSerializer.SerializeToElement(500 - VacantUnits), (500 - VacantUnits).ToString(), "int32"),
                new(JsonSerializer.SerializeToElement(VacantUnits), VacantUnits.ToString(), "int32")
            };

            return new(new(columns, [new(ReportRowKind.Detail, cells)]), 0, 1, 1, false, null);
        }
    }
}
