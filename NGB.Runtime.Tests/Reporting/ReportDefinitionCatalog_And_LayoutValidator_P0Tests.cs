using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportDefinitionCatalog_And_LayoutValidator_P0Tests
{
    [Fact]
    public void Catalog_Rejects_Duplicate_Report_Codes()
    {
        var act = () => new ReportDefinitionCatalog([
            new StubDefinitionSource(BuildComposableDefinition(" test.report ")),
            new StubDefinitionSource(BuildComposableDefinition("TEST.REPORT"))
        ]);

        act.Should().Throw<NgbInvariantViolationException>()
            .WithMessage("*Duplicate report code*'test.report'*");
    }

    [Fact]
    public void DatasetCatalog_Rejects_Duplicate_Dataset_Codes()
    {
        var act = () => new ReportDatasetCatalog([
            new StubDatasetSource(BuildLedgerDataset(" accounting.ledger.analysis ")),
            new StubDatasetSource(BuildLedgerDataset("ACCOUNTING.LEDGER.ANALYSIS"))
        ]);

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*Duplicate report dataset code*'accounting.ledger.analysis'*");
    }

    [Fact]
    public async Task DatasetCatalog_Returns_Registered_Dataset_By_Code()
    {
        var sut = new ReportDatasetCatalog([
            new StubDatasetSource(BuildLedgerDataset("accounting.ledger.analysis"))
        ]);

        var dataset = await sut.GetDatasetAsync("ACCOUNTING.LEDGER.ANALYSIS", CancellationToken.None);

        dataset.DatasetCode.Should().Be("accounting.ledger.analysis");
    }

    [Fact]
    public async Task DatasetCatalog_Unknown_Code_Throws_NotFound()
    {
        var sut = new ReportDatasetCatalog([]);

        var act = async () => await sut.GetDatasetAsync("missing.dataset", CancellationToken.None);

        await act.Should().ThrowAsync<ReportDatasetTypeNotFoundException>()
            .WithMessage("*missing.dataset*");
    }

    [Fact]
    public void DatasetDefinition_Rejects_Time_Grains_On_Non_Time_Field()
    {
        var dataset = new ReportDatasetDto(
            DatasetCode: "broken.dataset",
            Fields:
            [
                new ReportFieldDto("property_id", "Property", "uuid", ReportFieldKind.Dimension, IsGroupable: true, SupportedTimeGrains: [ReportTimeGrain.Month])
            ]);

        var act = () => new ReportDatasetDefinition(dataset);

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*declares time grains but is not a time field*");
    }

    [Fact]
    public void Validator_Rejects_Unknown_Row_Group_Field()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis");
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("unknown_field")],
                Measures: [new ReportMeasureSelectionDto("debit")]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*selected row grouping is no longer available in this report*");
    }

    [Fact]
    public void Validator_Rejects_Filter_Field_That_Is_Not_Filterable()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis");
        var request = new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["document_id"] = new(CreateJsonElement("abc"))
            },
            Layout: new ReportLayoutDto(
                Measures: [new ReportMeasureSelectionDto("debit")]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*'Document Id' cannot be used as a filter in this report.*");
    }

    [Fact]
    public void Validator_Rejects_Unsupported_Time_Grain_For_Grouping()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis");
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Quarter)],
                Measures: [new ReportMeasureSelectionDto("debit")]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*'Period' cannot be grouped by Quarter.*");
    }

    [Fact]
    public void Validator_Rejects_Sort_When_Time_Grain_Does_Not_Match_Selected_Row_Group()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis");
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Day)],
                Measures: [new ReportMeasureSelectionDto("debit")],
                Sorts: [new ReportSortDto("period_utc", ReportSortDirection.Asc, ReportTimeGrain.Month)]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*Sorting by Period (Month) is not available because the selected row grouping uses Period (Day).*");
    }

    [Fact]
    public void Validator_Rejects_Repeated_Time_Grouping_When_Not_Adjacent_Coarse_To_Fine()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis");
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                RowGroups:
                [
                    new ReportGroupingDto("period_utc", ReportTimeGrain.Day),
                    new ReportGroupingDto("account_code"),
                    new ReportGroupingDto("period_utc", ReportTimeGrain.Week)
                ],
                Measures: [new ReportMeasureSelectionDto("debit")]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*'Period' can be grouped more than once only from larger to smaller time buckets*");
    }

    [Fact]
    public void Validator_Rejects_Ambiguous_Sort_When_Time_Field_Is_Grouped_Multiple_Times()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis");
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                RowGroups:
                [
                    new ReportGroupingDto("period_utc", ReportTimeGrain.Month),
                    new ReportGroupingDto("period_utc", ReportTimeGrain.Day)
                ],
                Measures: [new ReportMeasureSelectionDto("debit")],
                Sorts: [new ReportSortDto("period_utc")]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*'Period' is grouped more than once on the row axis. Choose the exact grouped field, for example Period (Month) or Period (Day).*");
    }

    [Fact]
    public void Validator_Allows_Repeated_Time_Group_Sort_When_GroupKey_Selects_Exact_Grouping_Instance()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis");
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                RowGroups:
                [
                    new ReportGroupingDto("period_utc", ReportTimeGrain.Month, GroupKey: "row:month"),
                    new ReportGroupingDto("period_utc", ReportTimeGrain.Day, GroupKey: "row:day")
                ],
                Measures: [new ReportMeasureSelectionDto("debit")],
                Sorts: [new ReportSortDto("period_utc", ReportSortDirection.Asc, ReportTimeGrain.Day, false, "row:day")]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validator_Allows_Sort_By_Field_Selected_In_Column_Groupings()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis");
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("property_id")],
                ColumnGroups: [new ReportGroupingDto("account_code")],
                Measures: [new ReportMeasureSelectionDto("debit")],
                Sorts: [new ReportSortDto("account_code", ReportSortDirection.Asc, null, true)]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validator_Rejects_Sort_By_Unselected_Measure()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis");
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("account_code")],
                Measures: [new ReportMeasureSelectionDto("debit")],
                Sorts: [new ReportSortDto("credit", ReportSortDirection.Desc)]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*'Credit' is not selected as a measure in the current layout.*");
    }

    [Fact]
    public void Validator_Rejects_Column_Groups_For_Canonical_Report()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildCanonicalDefinition("accounting.balance_sheet");
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("amount")]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*This report does not allow column groupings.*");
    }

    [Fact]
    public void Validator_Requires_Measure_When_ColumnGroups_Selected()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis") with
        {
            Capabilities = (BuildComposableDefinition("accounting.ledger.analysis").Capabilities ?? new ReportCapabilitiesDto()) with
            {
                AllowsColumnGroups = true,
                MaxColumnGroupDepth = 3
            }
        };
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: []),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*Select at least one measure when column groupings are used.*");
    }

    [Fact]
    public void Validator_Rejects_Duplicate_Project_Output_When_Same_Field_Is_Selected_In_Row_And_Column_Groups()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis") with
        {
            Capabilities = (BuildComposableDefinition("accounting.ledger.analysis").Capabilities ?? new ReportCapabilitiesDto()) with
            {
                AllowsColumnGroups = true,
                MaxColumnGroupDepth = 3
            }
        };
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("account_code")],
                ColumnGroups: [new ReportGroupingDto("account_code")],
                Measures: [new ReportMeasureSelectionDto("debit")]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*already selected as a row grouping in the current layout*");
    }

    [Fact]
    public void Validator_Rejects_Duplicate_Project_Output_When_Detail_Field_Duplicates_Grouped_Field()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis");
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("account_code")],
                DetailFields: ["account_code"],
                Measures: [new ReportMeasureSelectionDto("debit")]),
            Offset: 0,
            Limit: 100);

        var act = () => sut.Validate(definition, request);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*already selected as a row grouping in the current layout*");
    }

    [Fact]
    public async Task Engine_Returns_Empty_Sheet_For_Valid_Skeleton_Request()
    {
        var engine = new ReportEngine(
            new ReportDefinitionCatalog([new StubDefinitionSource(BuildComposableDefinition("accounting.ledger.analysis"))]),
            new ReportLayoutValidator(),
            new ReportExecutionPlanner(),
            new StubPlanExecutor(),
            new ReportSheetBuilder());

        var response = await engine.ExecuteAsync(
            "accounting.ledger.analysis",
            new ReportExecutionRequestDto(
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_code")],
                    Measures: [new ReportMeasureSelectionDto("debit")],
                    ShowSubtotals: true,
                    ShowGrandTotals: true),
                Offset: 5,
                Limit: 20),
            CancellationToken.None);

        response.Offset.Should().Be(5);
        response.Limit.Should().Be(20);
        response.Sheet.Columns.Should().HaveCount(2);
        response.Sheet.Rows.Should().BeEmpty();
        response.Diagnostics.Should().NotBeNull();
        response.Diagnostics!["engine"].Should().Be("runtime");
    }

    [Fact]
    public void Planner_Builds_Normalized_Ast_From_Layout_And_Dataset_Metadata()
    {
        var runtime = new ReportDefinitionRuntimeModel(BuildComposableDefinition("accounting.ledger.analysis"));
        var request = new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["property_id"] = new(CreateJsonElement("00000000-0000-0000-0000-000000000001"), IncludeDescendants: true)
            },
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [" View "] = "compact"
            },
            Layout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto(" period_utc ", ReportTimeGrain.Month)],
                ColumnGroups: [new ReportGroupingDto(" party_id ")],
                Measures: [new ReportMeasureSelectionDto(" debit ", ReportAggregationKind.Sum, LabelOverride: "Debit Total")],
                DetailFields: [" document_display "],
                Sorts: [new ReportSortDto(" debit ", ReportSortDirection.Desc)],
                ShowDetails: true,
                ShowSubtotals: true,
                ShowGrandTotals: true),
            Offset: 10,
            Limit: 25,
            Cursor: "next:1");

        var context = new ReportExecutionContext(runtime, request, runtime.GetEffectiveLayout(request));
        var sut = new ReportExecutionPlanner();

        var plan = sut.BuildPlan(context);

        plan.ReportCode.Should().Be("accounting.ledger.analysis");
        plan.DatasetCode.Should().Be("accounting.ledger.analysis");
        plan.RowGroups.Should().ContainSingle();
        plan.RowGroups[0].FieldCode.Should().Be("period_utc");
        plan.RowGroups[0].OutputCode.Should().Be("period_utc__month");
        plan.RowGroups[0].Label.Should().Be("Period");
        plan.RowGroups[0].TimeGrain.Should().Be(ReportTimeGrain.Month);
        plan.ColumnGroups.Should().ContainSingle();
        plan.ColumnGroups[0].IsColumnAxis.Should().BeTrue();
        plan.Measures.Should().ContainSingle();
        plan.Measures[0].MeasureCode.Should().Be("debit");
        plan.Measures[0].OutputCode.Should().Be("debit__sum");
        plan.Measures[0].Label.Should().Be("Debit Total");
        plan.DetailFields.Should().ContainSingle();
        plan.DetailFields[0].FieldCode.Should().Be("document_display");
        plan.Predicates.Should().ContainSingle();
        plan.Predicates[0].FieldCode.Should().Be("property_id");
        plan.Predicates[0].Filter.IncludeDescendants.Should().BeTrue();
        plan.Parameters.Should().ContainSingle();
        plan.Parameters[0].ParameterCode.Should().Be("view");
        plan.Sorts.Should().ContainSingle();
        plan.Sorts[0].MeasureCode.Should().Be("debit");
        plan.Shape.IsPivot.Should().BeTrue();
        plan.Shape.ShowDetails.Should().BeTrue();
        plan.Paging.Offset.Should().Be(10);
        plan.Paging.Limit.Should().Be(25);
        plan.Paging.Cursor.Should().Be("next:1");
    }

    [Fact]
    public void Planner_Null_Context_Uses_Ngb_Argument_Required_Exception()
    {
        var sut = new ReportExecutionPlanner();

        var act = () => sut.BuildPlan(null!);

        act.Should().Throw<NgbArgumentRequiredException>()
            .WithMessage("*context*");
    }

    [Fact]
    public void Planner_Invalid_Runtime_State_Uses_Ngb_Invariant_Violation_Exception()
    {
        var runtime = new ReportDefinitionRuntimeModel(BuildComposableDefinition("accounting.ledger.analysis"));
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("missing_field")],
                Measures: [new ReportMeasureSelectionDto("debit")]),
            Offset: 0,
            Limit: 100);
        var context = new ReportExecutionContext(runtime, request, runtime.GetEffectiveLayout(request));
        var sut = new ReportExecutionPlanner();

        var act = () => sut.BuildPlan(context);

        act.Should().Throw<NgbInvariantViolationException>()
            .WithMessage("*cannot resolve dataset field 'missing_field'*");
    }

    [Fact]
    public void AddNgbRuntime_Registers_V2_Foundation_Services()
    {
        var services = new ServiceCollection();
        services.AddNgbRuntime();

        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IReportDefinitionProvider));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IReportDatasetCatalog));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IReportLayoutValidator));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IReportEngine));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ReportExecutionPlanner));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ReportSheetBuilder));
    }

    [Fact]
    public void DatasetDefinition_Null_Dto_Uses_Ngb_Configuration_Violation_Exception()
    {
        var act = () => new ReportDatasetDefinition(null!);

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*dataset definition is not configured*");
    }

    [Fact]
    public void RuntimeModel_Null_Definition_Uses_Ngb_Configuration_Violation_Exception()
    {
        var act = () => new ReportDefinitionRuntimeModel(null!);

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*definition is not configured*");
    }

    [Fact]
    public void LayoutValidator_Null_Request_Uses_Ngb_Argument_Required_Exception()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildComposableDefinition("accounting.ledger.analysis");

        var act = () => sut.Validate(definition, null!);

        act.Should().Throw<NgbArgumentRequiredException>()
            .WithMessage("*Request is required.*");
    }

    [Fact]
    public void SheetBuilder_Null_Plan_Uses_Ngb_Argument_Required_Exception()
    {
        var sut = new ReportSheetBuilder();
        var definition = new ReportDefinitionRuntimeModel(BuildComposableDefinition("accounting.ledger.analysis"));

        var act = () => sut.BuildEmptySheet(definition, null!);

        act.Should().Throw<NgbArgumentRequiredException>()
            .WithMessage("*plan*");
    }

    [Fact]
    public async Task Engine_Null_Request_Uses_Ngb_Argument_Required_Exception()
    {
        var engine = new ReportEngine(
            new ReportDefinitionCatalog([new StubDefinitionSource(BuildComposableDefinition("accounting.ledger.analysis"))]),
            new ReportLayoutValidator(),
            new ReportExecutionPlanner(),
            new StubPlanExecutor(),
            new ReportSheetBuilder());

        var act = async () => await engine.ExecuteAsync("accounting.ledger.analysis", null!, CancellationToken.None);

        await act.Should().ThrowAsync<NgbArgumentRequiredException>()
            .WithMessage("*Request is required.*");
    }

    [Fact]
    public void Engine_Null_Dependencies_Use_Ngb_Configuration_Violation_Exception()
    {
        var definitions = new ReportDefinitionCatalog([new StubDefinitionSource(BuildComposableDefinition("accounting.ledger.analysis"))]);
        var validator = new ReportLayoutValidator();
        var planner = new ReportExecutionPlanner();
        var sheetBuilder = new ReportSheetBuilder();

        Action act1 = () => new ReportEngine(null!, validator, planner, new StubPlanExecutor(), sheetBuilder);
        Action act2 = () => new ReportEngine(definitions, null!, planner, new StubPlanExecutor(), sheetBuilder);
        Action act3 = () => new ReportEngine(definitions, validator, null!, new StubPlanExecutor(), sheetBuilder);
        Action act4 = () => new ReportEngine(definitions, validator, planner, new StubPlanExecutor(), null!);

        act1.Should().Throw<NgbConfigurationViolationException>();
        act2.Should().Throw<NgbConfigurationViolationException>();
        act3.Should().Throw<NgbConfigurationViolationException>();
        act4.Should().Throw<NgbConfigurationViolationException>();
    }

    private static ReportDefinitionDto BuildComposableDefinition(string reportCode)
        => new(
            ReportCode: reportCode,
            Name: "Ledger Analysis",
            Group: "Accounting",
            Description: "Composable accounting ledger analysis.",
            Mode: ReportExecutionMode.Composable,
            Dataset: BuildLedgerDataset("accounting.ledger.analysis"),
            Capabilities: new ReportCapabilitiesDto(
                AllowsRowGroups: true,
                AllowsColumnGroups: true,
                AllowsMeasures: true,
                AllowsDetailFields: true,
                AllowsSorting: true,
                AllowsShowDetails: true,
                AllowsSubtotals: true,
                AllowsGrandTotals: true,
                MaxRowGroupDepth: 5,
                MaxColumnGroupDepth: 3),
            DefaultLayout: new ReportLayoutDto(
                Measures: [new ReportMeasureSelectionDto("debit")]),
            Filters:
            [
                new ReportFilterFieldDto("property_id", "Property", "uuid"),
                new ReportFilterFieldDto("party_id", "Party", "uuid")
            ]);

    private static ReportDefinitionDto BuildCanonicalDefinition(string reportCode)
        => new(
            ReportCode: reportCode,
            Name: "Balance Sheet",
            Group: "Accounting",
            Description: "Canonical accounting report.",
            Mode: ReportExecutionMode.Canonical,
            Dataset: new ReportDatasetDto(
                DatasetCode: "accounting.balance_sheet.dataset",
                Fields:
                [
                    new ReportFieldDto("period_utc", "Period", "date", ReportFieldKind.Time, IsFilterable: true, IsGroupable: true, IsSortable: true, SupportedTimeGrains: [ReportTimeGrain.Month, ReportTimeGrain.Year])
                ],
                Measures:
                [
                    new ReportMeasureDto("amount", "Amount", "decimal", [ReportAggregationKind.Sum])
                ]),
            Capabilities: new ReportCapabilitiesDto(
                AllowsRowGroups: false,
                AllowsColumnGroups: false,
                AllowsMeasures: true,
                AllowsDetailFields: false,
                AllowsSorting: false,
                AllowsShowDetails: false,
                AllowsSubtotals: false,
                AllowsGrandTotals: true),
            DefaultLayout: new ReportLayoutDto(
                Measures: [new ReportMeasureSelectionDto("amount")]));

    private static ReportDatasetDto BuildLedgerDataset(string datasetCode)
        => new(
            DatasetCode: datasetCode,
            Fields:
            [
                new ReportFieldDto("account_code", "Account", "string", ReportFieldKind.Dimension, IsFilterable: true, IsGroupable: true, IsSortable: true, IsSelectable: true),
                new ReportFieldDto("property_id", "Property", "uuid", ReportFieldKind.Dimension, IsFilterable: true, IsGroupable: true, IsSortable: true, IsSelectable: true),
                new ReportFieldDto("party_id", "Party", "uuid", ReportFieldKind.Dimension, IsFilterable: true, IsGroupable: true, IsSortable: true, IsSelectable: true),
                new ReportFieldDto("document_id", "Document Id", "uuid", ReportFieldKind.Detail, IsFilterable: false, IsGroupable: false, IsSortable: false, IsSelectable: true),
                new ReportFieldDto("document_display", "Document", "string", ReportFieldKind.Detail, IsFilterable: false, IsGroupable: false, IsSortable: false, IsSelectable: true),
                new ReportFieldDto("period_utc", "Period", "date", ReportFieldKind.Time, IsFilterable: true, IsGroupable: true, IsSortable: true, IsSelectable: true, SupportedTimeGrains: [ReportTimeGrain.Day, ReportTimeGrain.Week, ReportTimeGrain.Month, ReportTimeGrain.Year])
            ],
            Measures:
            [
                new ReportMeasureDto("debit", "Debit", "decimal", [ReportAggregationKind.Sum]),
                new ReportMeasureDto("credit", "Credit", "decimal", [ReportAggregationKind.Sum]),
                new ReportMeasureDto("net", "Net", "decimal", [ReportAggregationKind.Sum])
            ]);

    private static JsonElement CreateJsonElement(string value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.Clone();
    }

    private sealed class StubDefinitionSource(ReportDefinitionDto definition) : IReportDefinitionSource
    {
        public IReadOnlyList<ReportDefinitionDto> GetDefinitions() => [definition];
    }

    private sealed class StubDatasetSource(ReportDatasetDto dataset) : IReportDatasetSource
    {
        public IReadOnlyList<ReportDatasetDto> GetDatasets() => [dataset];
    }

    private sealed class StubPlanExecutor : IReportPlanExecutor
    {
        public Task<ReportDataPage> ExecuteAsync(ReportDefinitionDto definition, ReportExecutionRequestDto request, string reportCode, string? datasetCode, IReadOnlyList<ReportPlanGrouping> rowGroups, IReadOnlyList<ReportPlanGrouping> columnGroups, IReadOnlyList<ReportPlanFieldSelection> detailFields, IReadOnlyList<ReportPlanMeasure> measures, IReadOnlyList<ReportPlanSort> sorts, IReadOnlyList<ReportPlanPredicate> predicates, IReadOnlyList<ReportPlanParameter> parameters, ReportPlanPaging paging, CancellationToken ct)
            => Task.FromResult(new ReportDataPage([], [], paging.Offset, paging.Limit, 0, false, Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["executor"] = "stub" }));
    }

}
