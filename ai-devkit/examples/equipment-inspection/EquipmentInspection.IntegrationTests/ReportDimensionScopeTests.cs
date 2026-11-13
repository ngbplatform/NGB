using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class ReportDimensionScopeTests
{
    private const string ExecutePath = "/api/reports/" + InspectionReports.Summary + "/execute";

    [Fact]
    public async Task Ordered_scope_contributors_expand_only_requested_dimensions_after_report_authorization()
    {
        var first = new ScopeExpansion("first");
        var second = new ScopeExpansion("second");
        var calls = new List<string>();
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.AddSingleton<IReportDefinitionEnricher, EquipmentScopeDefinition>();
            services.AddSingleton<IReportDimensionScopeExpander>(new EquipmentScopeExpander(first, calls));
            services.AddSingleton<IReportDimensionScopeExpander>(new EquipmentScopeExpander(second, calls));
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = new List<Guid>();
        foreach (var name in new[] { "Scope parent", "Scope child", "Scope grandchild", "Outside scope" })
        {
            equipment.Add(await CreatePostedInspectionAsync(admin, name));
        }
        first.Source = equipment[0];
        first.Additions = [equipment[1]];
        second.Source = equipment[1];
        second.Additions = [equipment[2]];

        var direct = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, Request(equipment[0], false));
        Assert.Equal(1m, Total(direct));
        Assert.Empty(calls);
        var expanded = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, Request(equipment[0], true));
        Assert.Equal(3m, Total(expanded));
        Assert.Equal(new[] { "first", "second" }, calls);
        Assert.Equal(EquipmentDefinitions.EquipmentDimensionId, first.LastDimension);
        Assert.Equal(EquipmentDefinitions.EquipmentDimensionId, second.LastDimension);

        calls.Clear();
        var unrelated = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, Request(equipment[3], true));
        Assert.Equal(1m, Total(unrelated));
        Assert.Equal(new[] { "first", "second" }, calls);

        calls.Clear();
        using var reader = await host.CreateClientAsync("reader");
        using var denied = await reader.PostAsJsonAsync(ExecutePath, Request(equipment[0], true));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Empty(calls);

        await using var scope = host.Services.CreateAsyncScope();
        var expansion = scope.ServiceProvider.GetRequiredService<IDimensionScopeExpansionService>();
        Assert.Null(await expansion.ExpandAsync(InspectionReports.Summary, null));
        Assert.Same(DimensionScopeBag.Empty, await expansion.ExpandAsync(InspectionReports.Summary, DimensionScopeBag.Empty));
        Assert.Empty(calls);
        var original = new DimensionScopeBag([new DimensionScope(EquipmentDefinitions.EquipmentDimensionId, [equipment[0]], true)]);
        var unchanged = await expansion.ExpandAsync("example.unrelated-report", original);
        Assert.Same(original, unchanged);
        Assert.Empty(calls);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            expansion.ExpandAsync(InspectionReports.Summary, original, cancellation.Token));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Excessive_expansion_is_rejected_without_breaking_a_subsequent_bounded_report()
    {
        var expansion = new ScopeExpansion("bounded");
        var calls = new List<string>();
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.AddSingleton<IReportDefinitionEnricher, EquipmentScopeDefinition>();
            services.AddSingleton<IReportDimensionScopeExpander>(new EquipmentScopeExpander(expansion, calls));
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await CreatePostedInspectionAsync(admin, "Expansion limit");
        expansion.Source = equipment;
        expansion.Additions = Enumerable.Range(0, ReportLayoutLimits.MaxExpandedDimensionValues)
            .Select(_ => Guid.CreateVersion7()).ToArray();

        using var rejected = await admin.PostAsJsonAsync(ExecutePath, Request(equipment, true));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("Expanded dimension filters", await rejected.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "bounded" }, calls);

        expansion.Additions = [];
        var accepted = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, Request(equipment, true));
        Assert.Equal(1m, Total(accepted));
        Assert.Equal(new[] { "bounded", "bounded" }, calls);
    }

    [Fact]
    public async Task Expansion_requires_explicit_support_in_the_report_filter_definition()
    {
        var expansion = new ScopeExpansion("unconfigured");
        var calls = new List<string>();
        await using var host = new InspectionHost(configureServices: services =>
            services.AddSingleton<IReportDimensionScopeExpander>(new EquipmentScopeExpander(expansion, calls)));
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var rejected = await admin.PostAsJsonAsync(ExecutePath, Request(Guid.CreateVersion7(), true));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("does not support including child items", await rejected.Content.ReadAsStringAsync());
        Assert.Empty(calls);
    }

    private static async Task<Guid> CreatePostedInspectionAsync(HttpClient admin, string name)
    {
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", name)));
        var document = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
            CompleteInspectionPayload(equipment.Id));
        var path = "/api/documents/example.inspection/" + document.Id;
        var state = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var posted = await ExecutePostAsync(admin, path, state.DocumentVersion, document.Id.ToString());
        await EnsureSuccessAsync(posted);
        return equipment.Id;
    }

    private static ReportExecutionRequestDto Request(Guid equipment, bool descendants)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["equipment_id"] = new(JsonSerializer.SerializeToElement(new[] { equipment }), descendants)
            },
            Parameters: new Dictionary<string, string>
            {
                ["from_utc"] = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["to_utc"] = today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
    }

    private static decimal Total(ReportExecutionResponseDto response)
        => response.Sheet.Rows.Single(row => row.RowKind == ReportRowKind.Total).Cells[^1].Value!.Value.GetDecimal();

    private sealed class ScopeExpansion(string name)
    {
        public string Name { get; } = name;
        public Guid Source { get; set; }
        public IReadOnlyList<Guid> Additions { get; set; } = [];
        public Guid? LastDimension { get; set; }
    }

    private sealed class EquipmentScopeDefinition : IReportDefinitionEnricher
    {
        public ReportDefinitionDto Enrich(ReportDefinitionDto definition)
        {
            if (definition.ReportCode != InspectionReports.Summary)
            {
                return definition;
            }

            return definition with
            {
                Filters = definition.Filters!.Select(filter => filter.FieldCode == "equipment_id"
                    ? filter with { SupportsIncludeDescendants = true }
                    : filter).ToArray(),
                Dataset = definition.Dataset! with
                {
                    Fields = definition.Dataset.Fields!.Select(field => field.Code == "equipment_id"
                        ? field with { SupportsIncludeDescendants = true }
                        : field).ToArray()
                }
            };
        }
    }

    private sealed class EquipmentScopeExpander(ScopeExpansion expansion, List<string> calls) : IReportDimensionScopeExpander
    {
        public Task<DimensionScopeBag> ExpandAsync(string reportCode, DimensionScopeBag scopes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (reportCode != InspectionReports.Summary)
            {
                return Task.FromResult(scopes);
            }

            calls.Add(expansion.Name);
            var result = scopes.Select(scope =>
            {
                expansion.LastDimension = scope.DimensionId;
                return scope.DimensionId == EquipmentDefinitions.EquipmentDimensionId && scope.IncludeDescendants
                    && scope.ValueIds.Contains(expansion.Source)
                    ? new DimensionScope(scope.DimensionId, scope.ValueIds.Concat(expansion.Additions), true)
                    : scope;
            });
            return Task.FromResult(new DimensionScopeBag(result));
        }
    }
}
