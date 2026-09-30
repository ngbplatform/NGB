using System.Text.Json;
using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportLayoutValidatorFullCoverageTests
{
    private readonly ReportLayoutValidator _sut = new();

    [Fact]
    public void Validate_RejectsMissingPublicArguments()
    {
        ((Action)(() => _sut.Validate(null!, new ReportExecutionRequestDto())))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("definition");
        ((Action)(() => _sut.Validate(Definition(), null!)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("request");
    }

    [Fact]
    public void Validate_RequiredParameterUsesFriendlyFallbackAndAcceptsPresentValue()
    {
        var definition = Definition() with
        {
            Parameters = [new ReportParameterMetadataDto("required_value", "string", true, Label: " ")]
        };

        AssertInvalid(definition, new ReportExecutionRequestDto(), "parameters.required_value")
            .Message.Should().Contain("'Required Value' is required");

        _sut.Invoking(validator => validator.Validate(
                definition,
                new ReportExecutionRequestDto(Parameters: new Dictionary<string, string>
                {
                    ["required_value"] = "present"
                })))
            .Should().NotThrow();

        var labeled = definition with
        {
            Parameters = [new ReportParameterMetadataDto("labeled", "string", true, Label: "Labeled parameter")]
        };
        AssertInvalid(labeled, new ReportExecutionRequestDto(), "parameters.labeled")
            .Message.Should().Contain("'Labeled parameter' is required");

        _sut.Invoking(validator => validator.Validate(Definition() with { Filters = null }, new ReportExecutionRequestDto()))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_IncludeDescendantsUsesMetadataCapabilityAndFriendlyLabel()
    {
        var unsupported = Definition() with
        {
            Filters = [new ReportFilterFieldDto("filterable", " ", "uuid", SupportsIncludeDescendants: false)]
        };
        var request = FilterRequest("filterable", includeDescendants: true);

        AssertInvalid(unsupported, request, "filters.filterable")
            .Message.Should().Contain("'Filterable' does not support including child items");

        var supported = unsupported with
        {
            Filters = [new ReportFilterFieldDto("filterable", "Filter", "uuid", SupportsIncludeDescendants: true)]
        };
        _sut.Invoking(validator => validator.Validate(supported, request)).Should().NotThrow();
    }

    [Fact]
    public void Validate_CoversEveryCapabilityAndDepthGuard()
    {
        var baseCapabilities = AllCapabilities();
        var cases = new (ReportCapabilitiesDto Capabilities, ReportLayoutDto Layout, string Path)[]
        {
            (baseCapabilities with { AllowsRowGroups = false }, Layout(rowGroups: [new("group_ok")]), "layout.rowGroups"),
            (baseCapabilities with { AllowsColumnGroups = false }, Layout(columnGroups: [new("group_ok")], measures: [new("amount")]), "layout.columnGroups"),
            (baseCapabilities with { AllowsMeasures = false }, Layout(measures: [new("amount")]), "layout.measures"),
            (baseCapabilities with { AllowsDetailFields = false }, Layout(detailFields: ["detail"]), "layout.detailFields"),
            (baseCapabilities with { AllowsSorting = false }, Layout(sorts: [new("group_ok")]), "layout.sorts"),
            (baseCapabilities with { AllowsShowDetails = false }, Layout(showDetails: true), "layout.showDetails"),
            (baseCapabilities with { AllowsSubtotals = false }, Layout(rowGroups: [new("group_ok")], showSubtotals: true), "layout.showSubtotals"),
            (baseCapabilities with { AllowsGrandTotals = false }, Layout(showGrandTotals: true), "layout.showGrandTotals"),
            (baseCapabilities with { MaxRowGroupDepth = 1 }, Layout(rowGroups: [new("group_ok"), new("filterable")]), "layout.rowGroups"),
            (baseCapabilities with { MaxRowGroupDepth = 2 }, Layout(rowGroups: [new("group_ok"), new("filterable"), new("time", ReportTimeGrain.Month)]), "layout.rowGroups"),
            (baseCapabilities with { MaxColumnGroupDepth = 1 }, Layout(columnGroups: [new("group_ok"), new("filterable")], measures: [new("amount")]), "layout.columnGroups"),
            (baseCapabilities with { MaxColumnGroupDepth = 2 }, Layout(columnGroups: [new("group_ok"), new("filterable"), new("time", ReportTimeGrain.Month)], measures: [new("amount")]), "layout.columnGroups"),
            (baseCapabilities, Layout(columnGroups: [new("group_ok")]), "layout.measures")
        };

        foreach (var (capabilities, layout, path) in cases)
            AssertInvalid(Definition() with { Capabilities = capabilities }, new ReportExecutionRequestDto(Layout: layout), path);

        var canonical = Definition() with { Mode = ReportExecutionMode.Canonical };
        AssertInvalid(
            canonical,
            new ReportExecutionRequestDto(Layout: Layout(columnGroups: [new("group_ok")], measures: [new("amount")])),
            "layout.columnGroups");
    }

    [Fact]
    public void Validate_CanonicalWithoutDataset_CoversUnknownRequiredAndPresentFilters()
    {
        var definition = new ReportDefinitionDto(
            "canonical.report",
            "Canonical",
            Mode: ReportExecutionMode.Canonical,
            Capabilities: AllCapabilities(),
            Filters:
            [
                new ReportFilterFieldDto("account_id", " ", "uuid", IsRequired: true),
                new ReportFilterFieldDto("optional_id", "Optional", "uuid")
            ]);

        AssertInvalid(definition, new ReportExecutionRequestDto(), "filters.account_id")
            .Message.Should().Contain("'Account Id' is required");
        AssertInvalid(definition, FilterRequest("unknown__code"), "filters.unknown__code")
            .Message.Should().Contain("'Unknown Code' is not available");

        _sut.Invoking(validator => validator.Validate(
                definition,
                new ReportExecutionRequestDto(Filters: new Dictionary<string, ReportFilterValueDto>
                {
                    [" ACCOUNT_ID "] = new(JsonSerializer.SerializeToElement(Guid.NewGuid())),
                    ["optional_id"] = new(JsonSerializer.SerializeToElement(Guid.NewGuid()))
                })))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_DatasetSelections_CoverAllUnknownAndUnsupportedCases()
    {
        var definition = Definition();
        var cases = new (ReportLayoutDto Layout, string Path)[]
        {
            (Layout(rowGroups: [new("missing")]), "layout.rowGroups[0].fieldCode"),
            (Layout(columnGroups: [new("missing")], measures: [new("amount")]), "layout.columnGroups[0].fieldCode"),
            (Layout(rowGroups: [new("non_group")]), "layout.rowGroups[0].fieldCode"),
            (Layout(columnGroups: [new("non_group")], measures: [new("amount")]), "layout.columnGroups[0].fieldCode"),
            (Layout(rowGroups: [new("time", (ReportTimeGrain)777)]), "layout.rowGroups[0].timeGrain"),
            (Layout(columnGroups: [new("time", (ReportTimeGrain)777)], measures: [new("amount")]), "layout.columnGroups[0].timeGrain"),
            (Layout(measures: [new("missing")]), "layout.measures[0].measureCode"),
            (Layout(measures: [new("amount", ReportAggregationKind.Average, "Custom amount")]), "layout.measures[0].aggregation"),
            (Layout(detailFields: ["missing"]), "layout.detailFields[0]"),
            (Layout(detailFields: ["non_select"]), "layout.detailFields[0]"),
            (Layout(sorts: [new("non_sort")]), "layout.sorts[0].fieldCode"),
            (Layout(rowGroups: [new("time", ReportTimeGrain.Month)], sorts: [new("time", TimeGrain: (ReportTimeGrain)777)]), "layout.sorts[0].timeGrain"),
            (Layout(sorts: [new("missing")]), "layout.sorts[0].fieldCode")
        };

        foreach (var (layout, path) in cases)
            AssertInvalid(definition, new ReportExecutionRequestDto(Layout: layout), path);
    }

    [Fact]
    public void Validate_RejectsTooManyAndDuplicateSorts()
    {
        var definition = Definition();
        var tooMany = Enumerable.Range(0, ReportLayoutLimits.MaxSorts + 1)
            .Select(_ => new ReportSortDto("amount"))
            .ToArray();

        AssertInvalid(
            definition,
            new ReportExecutionRequestDto(Layout: Layout(measures: [new("amount")], sorts: tooMany)),
            "layout.sorts");

        AssertInvalid(
            definition,
            new ReportExecutionRequestDto(Layout: Layout(
                measures: [new("amount")],
                sorts: [new("amount"), new(" AMOUNT ")])),
            "layout.sorts[1]");
    }

    [Fact]
    public void Validate_RejectsOversizedRequestCollectionsBeforeSemanticPlanning()
    {
        var definition = Definition(maxRowDepth: ReportLayoutLimits.MaxRowGroups + 1);

        AssertInvalid(
            definition,
            new ReportExecutionRequestDto(Layout: Layout(rowGroups: Enumerable
                .Range(0, ReportLayoutLimits.MaxRowGroups + 1)
                .Select(_ => new ReportGroupingDto("group_ok"))
                .ToArray())),
            "layout.rowGroups");

        var filters = Enumerable.Range(0, ReportLayoutLimits.MaxFilters + 1)
            .ToDictionary(
                index => $"filter_{index}",
                index => new ReportFilterValueDto(JsonSerializer.SerializeToElement(index)));
        AssertInvalid(definition, new ReportExecutionRequestDto(Filters: filters), "filters");

        var parameters = Enumerable.Range(0, ReportLayoutLimits.MaxParameters + 1)
            .ToDictionary(index => $"parameter_{index}", _ => "value");
        AssertInvalid(definition, new ReportExecutionRequestDto(Parameters: parameters), "parameters");

        var tooManyValues = Enumerable.Range(0, ReportLayoutLimits.MaxValuesPerFilter + 1).ToArray();
        AssertInvalid(
            definition,
            new ReportExecutionRequestDto(Filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["filterable"] = new(JsonSerializer.SerializeToElement(tooManyValues))
            }),
            "filters.filterable");

        var duplicatedNormalizedCode = new Dictionary<string, ReportFilterValueDto>(StringComparer.Ordinal)
        {
            ["filterable"] = new(JsonSerializer.SerializeToElement(1)),
            [" FILTERABLE "] = new(JsonSerializer.SerializeToElement(2))
        };
        AssertInvalid(
            definition,
            new ReportExecutionRequestDto(Filters: duplicatedNormalizedCode),
            "filters.filterable");

        var tooManyTotalValues = Enumerable.Range(0, 5).ToDictionary(
            index => $"filter_{index}",
            _ => new ReportFilterValueDto(JsonSerializer.SerializeToElement(
                Enumerable.Range(0, ReportLayoutLimits.MaxValuesPerFilter).ToArray())));
        AssertInvalid(
            definition,
            new ReportExecutionRequestDto(Filters: tooManyTotalValues),
            "filters");

        AssertInvalid(
            definition,
            new ReportExecutionRequestDto(Parameters: new Dictionary<string, string>
            {
                ["value"] = new string('x', ReportLayoutLimits.MaxParameterValueLength + 1)
            }),
            "parameters.value");

        var duplicatedParameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["value"] = "first",
            [" VALUE "] = "second"
        };
        AssertInvalid(
            definition,
            new ReportExecutionRequestDto(Parameters: duplicatedParameters),
            "parameters.value");

        var nullableParameterDefinition = definition with
        {
            Parameters = [new ReportParameterMetadataDto("value", "string", false)]
        };
        _sut.Invoking(validator => validator.Validate(
                nullableParameterDefinition,
                new ReportExecutionRequestDto(Parameters: new Dictionary<string, string>
                {
                    ["value"] = null!
                })))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_FilterErrorsResolveMetadataDatasetAndFriendlyLabels()
    {
        var definition = Definition() with
        {
            Filters =
            [
                new ReportFilterFieldDto("non_group", "Metadata label", "string"),
                new ReportFilterFieldDto("non_sort", " ", "string")
            ]
        };

        AssertInvalid(definition, FilterRequest("non_group"), "filters.non_group")
            .Message.Should().Contain("'Metadata label' cannot be used");
        AssertInvalid(definition, FilterRequest("non_sort"), "filters.non_sort")
            .Message.Should().Contain("'Non Sort' cannot be used");
        AssertInvalid(definition with { Filters = [] }, FilterRequest("non_select"), "filters.non_select")
            .Message.Should().Contain("'Non selectable' cannot be used");
        AssertInvalid(definition with { Filters = [] }, FilterRequest("x"), "filters.x")
            .Message.Should().Contain("'X' cannot be used");
        AssertInvalid(definition with { Filters = [] }, FilterRequest("utc"), "filters.utc")
            .Message.Should().Contain("'Value' cannot be used");

        var canonical = new ReportDefinitionDto(
            "canonical.friendly",
            "Canonical",
            Mode: ReportExecutionMode.Canonical,
            Filters: []);
        AssertInvalid(canonical, FilterRequest("__"), "filters.__")
            .Message.Should().Contain("'Value' is not available");
    }

    [Fact]
    public void Validate_ProjectOutputDuplicatesReportEveryExistingSelectionKind()
    {
        var definition = Definition();
        var layouts = new[]
        {
            Layout(columnGroups: [new("group_ok")], measures: [new("amount")], detailFields: ["group_ok"]),
            Layout(detailFields: ["detail", "detail"]),
            Layout(measures: [new("amount"), new("amount", LabelOverride: "Again")])
        };

        AssertInvalid(definition, new ReportExecutionRequestDto(Layout: layouts[0]), "layout.detailFields[0]")
            .Message.Should().Contain("column grouping");
        AssertInvalid(definition, new ReportExecutionRequestDto(Layout: layouts[1]), "layout.detailFields[1]")
            .Message.Should().Contain("detail field");
        AssertInvalid(definition, new ReportExecutionRequestDto(Layout: layouts[2]), "layout.measures[1].measureCode")
            .Message.Should().Contain("measure");
    }

    [Fact]
    public void Validate_RepeatedTimeHierarchyCoversAllRanksNullAndInvalidEnumBoundary()
    {
        var definition = Definition(maxRowDepth: 8);
        var invalidNull = Layout(rowGroups:
        [
            new("time", ReportTimeGrain.Month),
            new("time")
        ]);
        AssertInvalid(definition, new ReportExecutionRequestDto(Layout: invalidNull), "layout.rowGroups[1].fieldCode");

        AssertInvalid(definition, new ReportExecutionRequestDto(Layout: Layout(rowGroups:
        [
            new("time", ReportTimeGrain.Year),
            new("group_ok"),
            new("time", ReportTimeGrain.Month)
        ])), "layout.rowGroups[2].fieldCode");
        AssertInvalid(definition, new ReportExecutionRequestDto(Layout: Layout(rowGroups:
        [
            new("time", ReportTimeGrain.Day),
            new("time", ReportTimeGrain.Month)
        ])), "layout.rowGroups[1].fieldCode");

        var validAllRanks = Layout(rowGroups:
        [
            new("time", ReportTimeGrain.Year, GroupKey: " year "),
            new("time", ReportTimeGrain.Quarter),
            new("time", ReportTimeGrain.Month),
            new("time", ReportTimeGrain.Week),
            new("time", ReportTimeGrain.Day),
            new("time", (ReportTimeGrain)999, GroupKey: " ")
        ]);
        _sut.Invoking(validator => validator.Validate(definition, new ReportExecutionRequestDto(Layout: validAllRanks)))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_FieldSortSelection_CoversEveryTargetingOutcome()
    {
        var definition = Definition(maxRowDepth: 5);

        AssertInvalid(definition, RequestWithSort(
            [new("time", ReportTimeGrain.Month, GroupKey: "month")],
            [],
            new("time", TimeGrain: ReportTimeGrain.Month, GroupKey: "missing")), "layout.sorts[0].groupKey");
        AssertInvalid(definition, RequestWithSort(
            [new("time", ReportTimeGrain.Month, GroupKey: "month")],
            [],
            new("group_ok", GroupKey: "month")), "layout.sorts[0].fieldCode");
        AssertInvalid(definition, RequestWithSort(
            [new("time", ReportTimeGrain.Month, GroupKey: "month")],
            [],
            new("time", TimeGrain: ReportTimeGrain.Day, GroupKey: "month")), "layout.sorts[0].timeGrain");
        _sut.Validate(definition, RequestWithSort(
            [new("time", ReportTimeGrain.Month, GroupKey: "month")],
            [],
            new("time", TimeGrain: ReportTimeGrain.Month, GroupKey: " month ")));

        var repeatedGroups = new[]
        {
            new ReportGroupingDto("time", ReportTimeGrain.Month),
            new ReportGroupingDto("time", ReportTimeGrain.Day)
        };
        _sut.Validate(definition, RequestWithSort(repeatedGroups, [], new("time", TimeGrain: ReportTimeGrain.Day)));
        AssertInvalid(definition, RequestWithSort(
            repeatedGroups,
            [],
            new("time", TimeGrain: ReportTimeGrain.Quarter)), "layout.sorts[0].groupKey");
        AssertInvalid(definition, RequestWithSort(
            repeatedGroups,
            [],
            new("time")), "layout.sorts[0].groupKey");

        AssertInvalid(definition, RequestWithSort(
            [],
            [],
            new("group_ok", AppliesToColumnAxis: true)), "layout.sorts[0].fieldCode");
        _sut.Validate(definition, RequestWithSort([], ["detail"], new("detail")));
        AssertInvalid(definition, RequestWithSort(
            [],
            ["time"],
            new("time", TimeGrain: ReportTimeGrain.Day)), "layout.sorts[0].timeGrain");
        AssertInvalid(definition, RequestWithSort([], [], new("group_ok")), "layout.sorts[0].fieldCode");

        _sut.Validate(definition, RequestWithSort(
            [new("time", ReportTimeGrain.Month)],
            [],
            new("time", TimeGrain: ReportTimeGrain.Month)));
        AssertInvalid(definition, RequestWithSort(
            [new("time", ReportTimeGrain.Month)],
            [],
            new("time", TimeGrain: ReportTimeGrain.Day)), "layout.sorts[0].timeGrain");
        AssertInvalid(definition, RequestWithSort(
            [new("time", ReportTimeGrain.Month)],
            [],
            new("time")), "layout.sorts[0].timeGrain");
        _sut.Validate(definition, new ReportExecutionRequestDto(Layout: Layout(
            measures: [new("amount")],
            sorts: [new("amount")])));
        AssertInvalid(definition, new ReportExecutionRequestDto(Layout: Layout(
            measures: [new("amount")],
            sorts: [new("other")])), "layout.sorts[0].fieldCode");
    }

    private ReportLayoutValidationException AssertInvalid(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string fieldPath)
    {
        var exception = _sut.Invoking(validator => validator.Validate(definition, request))
            .Should().Throw<ReportLayoutValidationException>().Which;
        exception.Context["fieldPath"].Should().Be(fieldPath);
        return exception;
    }

    private static ReportExecutionRequestDto RequestWithSort(
        IReadOnlyList<ReportGroupingDto> rowGroups,
        IReadOnlyList<string> details,
        ReportSortDto sort)
        => new(Layout: Layout(rowGroups: rowGroups, detailFields: details, sorts: [sort]));

    private static ReportExecutionRequestDto FilterRequest(string code, bool includeDescendants = false)
        => new(Filters: new Dictionary<string, ReportFilterValueDto>
        {
            [code] = new(JsonSerializer.SerializeToElement("value"), includeDescendants)
        });

    private static ReportLayoutDto Layout(
        IReadOnlyList<ReportGroupingDto>? rowGroups = null,
        IReadOnlyList<ReportGroupingDto>? columnGroups = null,
        IReadOnlyList<ReportMeasureSelectionDto>? measures = null,
        IReadOnlyList<string>? detailFields = null,
        IReadOnlyList<ReportSortDto>? sorts = null,
        bool showDetails = false,
        bool showSubtotals = false,
        bool showGrandTotals = false)
        => new(
            RowGroups: rowGroups,
            ColumnGroups: columnGroups,
            Measures: measures,
            DetailFields: detailFields,
            Sorts: sorts,
            ShowDetails: showDetails,
            ShowSubtotals: showSubtotals,
            ShowSubtotalsOnSeparateRows: false,
            ShowGrandTotals: showGrandTotals);

    private static ReportDefinitionDto Definition(int maxRowDepth = 8)
        => new(
            "test.layout.full",
            "Layout",
            Mode: ReportExecutionMode.Composable,
            Dataset: Dataset(),
            Capabilities: AllCapabilities() with { MaxRowGroupDepth = maxRowDepth },
            DefaultLayout: Layout(),
            Filters: [new ReportFilterFieldDto("filterable", "Filterable", "string")]);

    private static ReportCapabilitiesDto AllCapabilities()
        => new(
            AllowsFilters: true,
            AllowsRowGroups: true,
            AllowsColumnGroups: true,
            AllowsMeasures: true,
            AllowsDetailFields: true,
            AllowsSorting: true,
            AllowsShowDetails: true,
            AllowsSubtotals: true,
            AllowsSeparateRowSubtotals: true,
            AllowsGrandTotals: true,
            MaxRowGroupDepth: 8,
            MaxColumnGroupDepth: 8);

    private static ReportDatasetDto Dataset()
        => new(
            "test.layout.dataset",
            Fields:
            [
                new ReportFieldDto("group_ok", "Group", "string", ReportFieldKind.Dimension, IsGroupable: true, IsSortable: true, IsSelectable: true),
                new ReportFieldDto("filterable", "Filterable", "string", ReportFieldKind.Dimension, IsFilterable: true, IsGroupable: true, IsSortable: true, IsSelectable: true),
                new ReportFieldDto("non_group", "Not groupable", "string", ReportFieldKind.Dimension, IsGroupable: false, IsSelectable: true),
                new ReportFieldDto("detail", "Detail", "string", ReportFieldKind.Detail, IsSelectable: true, IsSortable: true),
                new ReportFieldDto("non_select", "Non selectable", "string", ReportFieldKind.Detail),
                new ReportFieldDto("non_sort", "Not sortable", "string", ReportFieldKind.Dimension, IsSelectable: true),
                new ReportFieldDto(
                    "time",
                    "Time",
                    "date",
                    ReportFieldKind.Time,
                    IsGroupable: true,
                    IsSortable: true,
                    IsSelectable: true,
                    SupportedTimeGrains:
                    [
                        ReportTimeGrain.Day,
                        ReportTimeGrain.Week,
                        ReportTimeGrain.Month,
                        ReportTimeGrain.Quarter,
                        ReportTimeGrain.Year,
                        (ReportTimeGrain)999
                    ])
            ],
            Measures:
            [
                new ReportMeasureDto("amount", "Amount", "decimal", [ReportAggregationKind.Sum]),
                new ReportMeasureDto("other", "Other", "decimal", [ReportAggregationKind.Sum])
            ]);
}
