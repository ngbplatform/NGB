using System.Text.Json;
using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportExecutionPlannerFullCoverageTests
{
    private readonly ReportExecutionPlanner _sut = new();

    [Fact]
    public void BuildPlan_RejectsNullContextDefinitionLayoutAndRequestWithDomainExceptions()
    {
        var definition = RuntimeDefinition();
        var request = new ReportExecutionRequestDto();
        var layout = new ReportLayoutDto();

        Action nullContext = () => _sut.BuildPlan(null!);
        Action nullDefinition = () => _sut.BuildPlan(new ReportExecutionContext(null!, request, layout));
        Action nullLayout = () => _sut.BuildPlan(new ReportExecutionContext(definition, request, null!));
        Action nullRequest = () => _sut.BuildPlan(new ReportExecutionContext(definition, null!, layout));

        nullContext.Should().Throw<NgbArgumentRequiredException>();
        nullDefinition.Should().Throw<NgbInvariantViolationException>().WithMessage("*definition*");
        nullLayout.Should().Throw<NgbInvariantViolationException>().WithMessage("*layout*");
        nullRequest.Should().Throw<NgbInvariantViolationException>().WithMessage("*request*");
    }

    [Fact]
    public void BuildPlan_NormalizesLabelsGroupKeysAndEverySortResolutionShape()
    {
        var runtime = RuntimeDefinition();
        var layout = new ReportLayoutDto(
            RowGroups:
            [
                new ReportGroupingDto("period", ReportTimeGrain.Month, LabelOverride: " ", GroupKey: null),
                new ReportGroupingDto("period", ReportTimeGrain.Year, LabelOverride: "Fiscal period", GroupKey: " year-group "),
                new ReportGroupingDto("category", IncludeDetails: true, IncludeEmpty: true, IncludeDescendants: true, GroupKey: "category-group")
            ],
            ColumnGroups:
            [
                new ReportGroupingDto("period", ReportTimeGrain.Quarter, LabelOverride: null, GroupKey: "   ")
            ],
            Measures:
            [
                new ReportMeasureSelectionDto("amount", ReportAggregationKind.Sum, LabelOverride: null),
                new ReportMeasureSelectionDto("amount", ReportAggregationKind.Min, LabelOverride: "Minimum", FormatOverride: "0.000")
            ],
            DetailFields: ["detail"],
            Sorts:
            [
                new ReportSortDto("period", GroupKey: " year-group "),
                new ReportSortDto("period", ReportSortDirection.Desc, GroupKey: "missing"),
                new ReportSortDto("period", AppliesToColumnAxis: true, GroupKey: "column:0"),
                new ReportSortDto("ungrouped", TimeGrain: ReportTimeGrain.Day),
                new ReportSortDto("category"),
                new ReportSortDto("period", TimeGrain: ReportTimeGrain.Month),
                new ReportSortDto("period", TimeGrain: ReportTimeGrain.Day),
                new ReportSortDto("period"),
                new ReportSortDto("amount", ReportSortDirection.Desc)
            ],
            ShowDetails: true,
            ShowSubtotals: true,
            ShowSubtotalsOnSeparateRows: true,
            ShowGrandTotals: true);
        var request = new ReportExecutionRequestDto(
            Layout: layout,
            Filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["category"] = new(JsonSerializer.SerializeToElement(Guid.CreateVersion7()), true)
            },
            Parameters: new Dictionary<string, string> { [" View "] = "compact" },
            Offset: 7,
            Limit: 13,
            Cursor: "cursor");

        var plan = _sut.BuildPlan(new ReportExecutionContext(runtime, request, layout));

        plan.RowGroups.Should().HaveCount(3);
        plan.RowGroups[0].Label.Should().Be("Period");
        plan.RowGroups[0].GroupKey.Should().Be("row:0");
        plan.RowGroups[1].Label.Should().Be("Fiscal period");
        plan.RowGroups[1].GroupKey.Should().Be("year-group");
        plan.RowGroups[2].Should().Match<NGB.Runtime.Reporting.Planning.ReportPlanGrouping>(x =>
            x.IncludeDetails && x.IncludeEmpty && x.IncludeDescendants && x.GroupKey == "category-group");
        plan.ColumnGroups.Should().ContainSingle();
        plan.ColumnGroups[0].GroupKey.Should().Be("column:0");
        plan.ColumnGroups[0].IsColumnAxis.Should().BeTrue();
        plan.Measures[0].Label.Should().Be("Amount");
        plan.Measures[1].Label.Should().Be("Minimum");
        plan.Measures[1].FormatOverride.Should().Be("0.000");
        plan.DetailFields.Should().ContainSingle(x => x.FieldCode == "detail");
        plan.Predicates.Should().ContainSingle(x => x.FieldCode == "category");
        plan.Parameters.Should().ContainSingle(x => x.ParameterCode == "view" && x.Value == "compact");
        plan.Sorts.Should().HaveCount(9);
        plan.Sorts[0].TimeGrain.Should().Be(ReportTimeGrain.Year);
        plan.Sorts[0].GroupKey.Should().Be("year-group");
        plan.Sorts[1].TimeGrain.Should().BeNull();
        plan.Sorts[1].GroupKey.Should().Be("missing");
        plan.Sorts[2].AppliesToColumnAxis.Should().BeTrue();
        plan.Sorts[2].TimeGrain.Should().Be(ReportTimeGrain.Quarter);
        plan.Sorts[3].TimeGrain.Should().Be(ReportTimeGrain.Day);
        plan.Sorts[4].GroupKey.Should().Be("category-group");
        plan.Sorts[5].GroupKey.Should().Be("row:0");
        plan.Sorts[6].GroupKey.Should().BeNull();
        plan.Sorts[7].GroupKey.Should().BeNull();
        plan.Sorts[8].MeasureCode.Should().Be("amount");
        plan.Shape.IsPivot.Should().BeTrue();
        plan.Paging.Should().Be(new NGB.Runtime.Reporting.Planning.ReportPlanPaging(7, 13, "cursor"));
    }

    [Fact]
    public void BuildPlan_CanonicalFilters_CoversNullMetadataNullFiltersUnknownAndKnownFields()
    {
        var noMetadata = CanonicalRuntime(filters: null);
        var emptyRequest = new ReportExecutionRequestDto(Layout: new ReportLayoutDto());
        var noMetadataPlan = _sut.BuildPlan(new ReportExecutionContext(noMetadata, emptyRequest, emptyRequest.Layout!));

        noMetadataPlan.Predicates.Should().BeEmpty();

        var knownValue = new ReportFilterValueDto(JsonSerializer.SerializeToElement("known"));
        var withMetadata = CanonicalRuntime(
        [
            new ReportFilterFieldDto("known", "Known filter", "string")
        ]);
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(),
            Filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["unknown"] = new(JsonSerializer.SerializeToElement(1)),
                [" Known "] = knownValue
            });

        var plan = _sut.BuildPlan(new ReportExecutionContext(withMetadata, request, request.Layout!));

        plan.Predicates.Should().ContainSingle();
        plan.Predicates[0].Should().BeEquivalentTo(new NGB.Runtime.Reporting.Planning.ReportPlanPredicate(
            "known", "known", "Known filter", "string", knownValue));
    }

    [Fact]
    public void BuildPlan_MissingDatasetFieldsMeasuresAndSorts_ThrowInvariant()
    {
        var runtime = RuntimeDefinition();
        var canonical = CanonicalRuntime(filters: null);

        AssertInvalid(runtime, new ReportLayoutDto(RowGroups: [new ReportGroupingDto("missing")]), "field 'missing'");
        AssertInvalid(runtime, new ReportLayoutDto(DetailFields: ["missing"]), "field 'missing'");
        AssertInvalid(runtime, new ReportLayoutDto(Measures: [new ReportMeasureSelectionDto("missing")]), "measure 'missing'");
        AssertInvalid(runtime, new ReportLayoutDto(Sorts: [new ReportSortDto("missing")]), "sort field 'missing'");
        AssertInvalid(canonical, new ReportLayoutDto(RowGroups: [new ReportGroupingDto("missing")]), "field 'missing'");
        AssertInvalid(canonical, new ReportLayoutDto(Measures: [new ReportMeasureSelectionDto("missing")]), "measure 'missing'");
        AssertInvalid(canonical, new ReportLayoutDto(Sorts: [new ReportSortDto("missing")]), "sort field 'missing'");
    }

    private void AssertInvalid(ReportDefinitionRuntimeModel runtime, ReportLayoutDto layout, string message)
    {
        var request = new ReportExecutionRequestDto(Layout: layout);
        Action action = () => _sut.BuildPlan(new ReportExecutionContext(runtime, request, layout));
        action.Should().Throw<NgbInvariantViolationException>().WithMessage($"*{message}*");
    }

    private static ReportDefinitionRuntimeModel RuntimeDefinition()
        => new(new ReportDefinitionDto(
            "test.planner",
            "Planner",
            Mode: ReportExecutionMode.Composable,
            Dataset: new ReportDatasetDto(
                "test.dataset",
                Fields:
                [
                    new ReportFieldDto("period", "Period", "datetime", ReportFieldKind.Time),
                    new ReportFieldDto("category", "Category", "uuid", ReportFieldKind.Dimension),
                    new ReportFieldDto("detail", "Detail", "string", ReportFieldKind.Detail),
                    new ReportFieldDto("ungrouped", "Ungrouped", "string", ReportFieldKind.Attribute)
                ],
                Measures:
                [
                    new ReportMeasureDto("amount", "Amount", "decimal", [ReportAggregationKind.Sum, ReportAggregationKind.Min])
                ])));

    private static ReportDefinitionRuntimeModel CanonicalRuntime(IReadOnlyList<ReportFilterFieldDto>? filters)
        => new(new ReportDefinitionDto(
            "test.canonical",
            "Canonical",
            Mode: ReportExecutionMode.Canonical,
            Filters: filters));
}
