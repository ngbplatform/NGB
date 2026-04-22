using System.Text.Json;
using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Contracts.Metadata;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportEngine_ScopeExpansion_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_Expands_IncludeDescendants_Filter_Before_Executor()
    {
        var propertyDimensionId = Guid.CreateVersion7();
        var buildingId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var executor = new CapturingPlanExecutor();

        var sut = new ReportEngine(
            new ReportDefinitionCatalog([new StubDefinitionSource()]),
            new ReportLayoutValidator(),
            new ReportExecutionPlanner(),
            executor,
            new ReportSheetBuilder(),
            null,
            new ReportFilterScopeExpander(
                new StubDimensionScopeExpansionService(propertyDimensionId, [buildingId, unitId]),
                new StubDimensionDefinitionReader(propertyDimensionId)));

        await sut.ExecuteAsync(
            "test.scope_report",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["property_id"] = new(JsonSerializer.SerializeToElement(buildingId), IncludeDescendants: true)
                },
                Layout: new ReportLayoutDto(
                    Measures: [new ReportMeasureSelectionDto("entry_count")],
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 50),
            CancellationToken.None);

        executor.CapturedPredicates.Should().ContainSingle();
        var predicate = executor.CapturedPredicates.Single();
        predicate.FieldCode.Should().Be("property_id");
        predicate.Filter.IncludeDescendants.Should().BeFalse();
        predicate.Filter.Value.ValueKind.Should().Be(JsonValueKind.Array);
        predicate.Filter.Value.EnumerateArray().Select(x => x.GetGuid()).Should().BeEquivalentTo([buildingId, unitId]);
    }

    private sealed class StubDefinitionSource : IReportDefinitionSource
    {
        public IReadOnlyList<ReportDefinitionDto> GetDefinitions()
            =>
            [
                new ReportDefinitionDto(
                    ReportCode: "test.scope_report",
                    Name: "Scope Report",
                    Mode: ReportExecutionMode.Composable,
                    Dataset: new ReportDatasetDto(
                        DatasetCode: "test.scope_dataset",
                        Fields:
                        [
                            new ReportFieldDto(
                                Code: "property_id",
                                Label: "Property",
                                DataType: "uuid",
                                Kind: ReportFieldKind.Dimension,
                                IsFilterable: true,
                                Lookup: new CatalogLookupSourceDto("test.property"))
                        ],
                        Measures:
                        [
                            new ReportMeasureDto(
                                Code: "entry_count",
                                Label: "Entries",
                                DataType: "int64",
                                SupportedAggregations: [ReportAggregationKind.Count])
                        ]),
                    DefaultLayout: new ReportLayoutDto(
                        Measures: [new ReportMeasureSelectionDto("entry_count")]))
            ];
    }

    private sealed class StubDimensionDefinitionReader(Guid propertyDimensionId) : IDimensionDefinitionReader
    {
        public Task<IReadOnlyDictionary<string, Guid>> GetDimensionIdsByCodesAsync(IReadOnlyCollection<string> dimensionCodes, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<string, Guid>>(new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["test.property"] = propertyDimensionId
            });
    }

    private sealed class StubDimensionScopeExpansionService(Guid propertyDimensionId, IReadOnlyList<Guid> expandedValueIds)
        : IDimensionScopeExpansionService
    {
        public Task<DimensionScopeBag?> ExpandAsync(string reportCode, DimensionScopeBag? scopes, CancellationToken ct = default)
        {
            scopes.Should().NotBeNull();
            scopes!.Should().ContainSingle();
            scopes[0].DimensionId.Should().Be(propertyDimensionId);
            scopes[0].IncludeDescendants.Should().BeTrue();

            return Task.FromResult<DimensionScopeBag?>(
                new DimensionScopeBag([new DimensionScope(propertyDimensionId, expandedValueIds, includeDescendants: false)]));
        }
    }

    private sealed class CapturingPlanExecutor : IReportPlanExecutor
    {
        public IReadOnlyList<ReportPlanPredicate> CapturedPredicates { get; private set; } = [];

        public Task<ReportDataPage> ExecuteAsync(
            ReportDefinitionDto definition,
            ReportExecutionRequestDto request,
            string reportCode,
            string? datasetCode,
            IReadOnlyList<ReportPlanGrouping> rowGroups,
            IReadOnlyList<ReportPlanGrouping> columnGroups,
            IReadOnlyList<ReportPlanFieldSelection> detailFields,
            IReadOnlyList<ReportPlanMeasure> measures,
            IReadOnlyList<ReportPlanSort> sorts,
            IReadOnlyList<ReportPlanPredicate> predicates,
            IReadOnlyList<ReportPlanParameter> parameters,
            ReportPlanPaging paging,
            CancellationToken ct)
        {
            CapturedPredicates = predicates;
            return Task.FromResult(new ReportDataPage(
                Columns: [new ReportDataColumn("entry_count__count", "Entries", "int64", "measure")],
                Rows: [new ReportDataRow(new Dictionary<string, object?> { ["entry_count__count"] = 2L })],
                Offset: paging.Offset,
                Limit: paging.Limit,
                Total: 1,
                HasMore: false));
        }
    }
}
