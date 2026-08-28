using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Definitions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportSheetBuilderFullCoverageTests
{
    [Fact]
    public void PublicOperations_RejectMissingRequiredArguments()
    {
        var builder = new ReportSheetBuilder();
        var definition = Definition();
        var plan = Plan(definition, new ReportLayoutDto(Measures: [new ReportMeasureSelectionDto("debit_amount")]));
        var page = Page();

        ((Action)(() => builder.BuildEmptySheet(null!, plan)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("definition");
        ((Action)(() => builder.BuildEmptySheet(definition, null!)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("plan");
        ((Action)(() => builder.BuildSheet(null!, plan, page)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("definition");
        ((Action)(() => builder.BuildSheet(definition, null!, page)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("plan");
        ((Action)(() => builder.BuildSheet(definition, plan, null!)))
            .Should().Throw<NgbInvariantViolationException>()
            .WithMessage("Reporting sheet builder requires a materialized data page.");
    }

    [Fact]
    public void BuildEmptySheet_NonPivot_ReturnsDescribedSkeleton()
    {
        var definition = Definition();
        var plan = Plan(
            definition,
            new ReportLayoutDto(
                DetailFields: ["document_display"],
                Measures: [new ReportMeasureSelectionDto("debit_amount")]));

        var sheet = new ReportSheetBuilder().BuildEmptySheet(definition, plan);

        sheet.Rows.Should().BeEmpty();
        sheet.Columns.Select(column => column.Code)
            .Should().Equal("document_display", "debit_amount__sum");
        sheet.Meta.Should().BeEquivalentTo(new ReportSheetMetaDto(
            Title: "Ledger Analysis",
            Subtitle: "Composable accounting ledger analysis",
            IsPivot: false,
            HasRowOutline: false,
            HasColumnGroups: false,
            Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode"] = "Composable",
                ["state"] = "skeleton"
            }));
    }

    [Fact]
    public void BuildEmptySheet_PivotSkeleton_CoversHierarchyDetailsAndSingleGrandTotal()
    {
        var definition = Definition();
        var plan = Plan(
            definition,
            new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("account_display")],
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                DetailFields: ["document_display"],
                ShowDetails: true,
                ShowGrandTotals: true));

        var sheet = new ReportSheetBuilder().BuildEmptySheet(definition, plan);

        sheet.Columns.Select(column => (column.Code, column.Title, column.SemanticRole)).Should().Equal(
            (ReportSheetBuilder.RowHierarchyColumnCode, "Account", "row-group"),
            ("document_display", "Document", "detail"),
            ("total_debit_amount__sum", "Total", "pivot-total"));
        sheet.Meta!.IsPivot.Should().BeTrue();
        sheet.Meta.HasRowOutline.Should().BeTrue();
        sheet.Meta.HasColumnGroups.Should().BeTrue();
    }

    [Fact]
    public void BuildEmptySheet_PivotSkeleton_CoversNoGrandTotalAndMultipleGrandTotals()
    {
        var definition = Definition();
        var withoutTotals = Plan(
            definition,
            new ReportLayoutDto(
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowGrandTotals: false));
        var withMultipleTotals = Plan(
            definition,
            new ReportLayoutDto(
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures:
                [
                    new ReportMeasureSelectionDto("debit_amount"),
                    new ReportMeasureSelectionDto("credit_amount")
                ],
                ShowGrandTotals: true));

        new ReportSheetBuilder().BuildEmptySheet(definition, withoutTotals)
            .Columns.Should().BeEmpty();

        var columns = new ReportSheetBuilder().BuildEmptySheet(definition, withMultipleTotals).Columns;
        columns.Select(column => (column.Code, column.Title)).Should().Equal(
            ("total_debit_amount__sum", "Total Debit"),
            ("total_credit_amount__sum", "Total Credit"));
    }

    [Fact]
    public void BuildSheet_PrebuiltWithoutMeta_UsesDefinitionAndEmptyState()
    {
        var definition = Definition();
        var plan = Plan(
            definition,
            new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("account_display")],
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("debit_amount")]));
        var prebuilt = new ReportSheetDto(
            [new ReportSheetColumnDto("amount", "Amount", "decimal")],
            []);

        var result = new ReportSheetBuilder().BuildSheet(
            definition,
            plan,
            Page(prebuiltSheet: prebuilt));

        result.Meta!.Title.Should().Be(definition.Definition.Name);
        result.Meta.Subtitle.Should().Be(definition.Definition.Description);
        result.Meta.IsPivot.Should().Be(plan.Shape.IsPivot);
        result.Meta.HasRowOutline.Should().BeTrue();
        result.Meta.HasColumnGroups.Should().BeTrue();
        result.Meta.Diagnostics.Should().Contain(new Dictionary<string, string>
        {
            ["mode"] = "Composable",
            ["state"] = "empty",
            ["sheetBuilder"] = "prebuilt-v1",
            ["renderedRows"] = "0",
            ["renderedColumns"] = "1"
        });
    }

    [Fact]
    public void BuildSheet_PrebuiltWithMeta_PreservesMetaAndMergesDiagnosticsWithPagePrecedence()
    {
        var definition = Definition();
        var plan = Plan(definition, new ReportLayoutDto(Measures: [new ReportMeasureSelectionDto("debit_amount")]));
        var suppliedMeta = new ReportSheetMetaDto(
            Title: "Supplied title",
            Subtitle: "Supplied subtitle",
            IsPivot: true,
            HasRowOutline: true,
            HasColumnGroups: true,
            Diagnostics: new Dictionary<string, string>
            {
                ["source"] = "sheet",
                ["sheetOnly"] = "yes"
            });
        var prebuilt = new ReportSheetDto(
            [new ReportSheetColumnDto("amount", "Amount", "decimal")],
            [new ReportSheetRowDto(ReportRowKind.Detail, [])],
            suppliedMeta);

        var result = new ReportSheetBuilder().BuildSheet(
            definition,
            plan,
            Page(
                diagnostics: new Dictionary<string, string>
                {
                    ["source"] = "page",
                    ["pageOnly"] = "yes"
                },
                prebuiltSheet: prebuilt));

        result.Meta.Should().NotBeNull();
        result.Meta!.Title.Should().Be("Supplied title");
        result.Meta.Subtitle.Should().Be("Supplied subtitle");
        result.Meta.IsPivot.Should().BeTrue();
        result.Meta.HasRowOutline.Should().BeTrue();
        result.Meta.HasColumnGroups.Should().BeTrue();
        result.Meta.Diagnostics.Should().Contain(new Dictionary<string, string>
        {
            ["source"] = "page",
            ["sheetOnly"] = "yes",
            ["pageOnly"] = "yes",
            ["state"] = "materialized"
        });
    }

    [Fact]
    public void BuildSheet_PrebuiltCanonicalSheet_EnforcesVisibleRowAndColumnCaps()
    {
        var source = Definition(ReportExecutionMode.Canonical, maxVisibleRows: 1);
        var definition = new ReportDefinitionRuntimeModel(source.Definition with
        {
            Capabilities = source.Definition.Capabilities! with
            {
                MaxVisibleRows = 1,
                MaxVisibleColumns = 1
            }
        });
        var plan = Plan(
            definition,
            new ReportLayoutDto(Measures: [new ReportMeasureSelectionDto("debit_amount")]))
            with { Mode = ReportExecutionMode.Canonical };

        var tooManyRows = () => new ReportSheetBuilder().BuildSheet(
            definition,
            plan,
            Page(prebuiltSheet: new ReportSheetDto(
                [new ReportSheetColumnDto("value", "Value", "string")],
                [
                    new ReportSheetRowDto(ReportRowKind.Detail, []),
                    new ReportSheetRowDto(ReportRowKind.Detail, [])
                ])));
        tooManyRows.Should().Throw<ReportLayoutValidationException>();

        var tooManyColumns = () => new ReportSheetBuilder().BuildSheet(
            definition,
            plan,
            Page(prebuiltSheet: new ReportSheetDto(
                [
                    new ReportSheetColumnDto("first", "First", "string"),
                    new ReportSheetColumnDto("second", "Second", "string")
                ],
                [])));
        tooManyColumns.Should().Throw<ReportLayoutValidationException>();
    }

    [Fact]
    public void BuildSheet_EmptyPivot_MergesPageDiagnostics()
    {
        var definition = Definition();
        var plan = Plan(
            definition,
            new ReportLayoutDto(
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowGrandTotals: true));

        var result = new ReportSheetBuilder().BuildSheet(
            definition,
            plan,
            Page(diagnostics: new Dictionary<string, string> { ["source"] = "page" }));

        result.Rows.Should().BeEmpty();
        result.Meta!.Diagnostics.Should().Contain(new Dictionary<string, string>
        {
            ["state"] = "empty",
            ["source"] = "page",
            ["pivotColumnLeafCount"] = "0"
        });
    }

    [Fact]
    public void BuildSheet_CanonicalMode_DoesNotApplyComposableVisibleRowCap()
    {
        var definition = Definition(ReportExecutionMode.Canonical, maxVisibleRows: 0);
        var plan = Plan(
            definition,
            new ReportLayoutDto(Measures: [new ReportMeasureSelectionDto("debit_amount")]))
            with { Mode = ReportExecutionMode.Canonical };

        var result = new ReportSheetBuilder().BuildSheet(
            definition,
            plan,
            Page(
                rows: [DataRow(("debit_amount__sum", 1m))],
                diagnostics: new Dictionary<string, string> { ["source"] = "canonical" }));

        result.Rows.Should().ContainSingle();
        result.Meta!.Diagnostics!["source"].Should().Be("canonical");
    }

    [Theory]
    [InlineData("detail", "layout.rowGroups")]
    [InlineData("column", "layout.columnGroups")]
    [InlineData("measure", "layout.measures")]
    public void BuildSheet_WhenVisibleRowCapExceeded_ReportsRelevantLayoutPath(
        string layoutKind,
        string expectedFieldPath)
    {
        var definition = Definition(maxVisibleRows: 0);
        var layout = layoutKind switch
        {
            "detail" => new ReportLayoutDto(
                DetailFields: ["document_display"],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowDetails: true),
            "column" => new ReportLayoutDto(
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("debit_amount")]),
            _ => new ReportLayoutDto(Measures: [new ReportMeasureSelectionDto("debit_amount")])
        };
        var plan = Plan(definition, layout);
        if (layoutKind == "column")
            plan = plan with { Shape = plan.Shape with { IsPivot = false } };
        var row = layoutKind switch
        {
            "detail" => DataRow(("document_display", "D-1"), ("debit_amount__sum", 1m)),
            "column" => DataRow(("period_utc__month", new DateOnly(2026, 1, 1)), ("debit_amount__sum", 1m)),
            _ => DataRow(("debit_amount__sum", 1m))
        };

        var act = () => new ReportSheetBuilder().BuildSheet(definition, plan, Page(rows: [row]));

        var exception = act.Should().Throw<ReportLayoutValidationException>().Which;
        exception.Context["fieldPath"].Should().Be(expectedFieldPath);
    }

    private static ReportDefinitionRuntimeModel Definition(
        ReportExecutionMode mode = ReportExecutionMode.Composable,
        int? maxVisibleRows = 5_000)
    {
        var definition = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();
        return new ReportDefinitionRuntimeModel(definition with
        {
            Mode = mode,
            Capabilities = definition.Capabilities! with { MaxVisibleRows = maxVisibleRows }
        });
    }

    private static ReportQueryPlan Plan(ReportDefinitionRuntimeModel definition, ReportLayoutDto layout)
    {
        var request = new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["from_utc"] = "2026-01-01",
                ["to_utc"] = "2026-12-31"
            },
            Layout: layout,
            Offset: 0,
            Limit: 50);

        return new ReportExecutionPlanner().BuildPlan(new ReportExecutionContext(definition, request, layout));
    }

    private static ReportDataPage Page(
        IReadOnlyList<ReportDataRow>? rows = null,
        IReadOnlyDictionary<string, string>? diagnostics = null,
        ReportSheetDto? prebuiltSheet = null)
        => new(
            Columns: [],
            Rows: rows ?? [],
            Offset: 0,
            Limit: 50,
            Total: rows?.Count ?? 0,
            HasMore: false,
            Diagnostics: diagnostics,
            PrebuiltSheet: prebuiltSheet);

    private static ReportDataRow DataRow(params (string Code, object? Value)[] values)
        => new(values.ToDictionary(pair => pair.Code, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
}
