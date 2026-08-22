using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Definitions;
using NGB.Runtime.Reporting.Rendering;
using NGB.Tools.Exceptions;
using Xunit;
using PivotPlanMeasure = NGB.Runtime.Reporting.Planning.ReportPlanMeasure;

namespace NGB.Runtime.Tests.Reporting.Rendering;

public sealed class ReportPivotMatrixBuilderFullCoverageTests
{
    [Fact]
    public void ConstructorAndBuild_RejectMissingDependenciesArgumentsAndColumnGroups()
    {
        var definition = Definition();
        var plan = Plan(definition, new ReportLayoutDto(
            ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
            Measures: [new ReportMeasureSelectionDto("debit_amount")]));
        var formatter = new ReportCellFormatter();
        var resolver = new ReportComposableCellActionResolver(plan, definition.Dataset);
        var headers = new ReportPivotHeaderBuilder(formatter, resolver);

        ((Action)(() => new ReportPivotMatrixBuilder(null!, headers, resolver)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("cellFormatter");
        ((Action)(() => new ReportPivotMatrixBuilder(formatter, null!, resolver)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("headerBuilder");
        ((Action)(() => new ReportPivotMatrixBuilder(formatter, headers, null!)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("actionResolver");

        var sut = new ReportPivotMatrixBuilder(formatter, headers, resolver);
        ((Action)(() => sut.Build(null!, plan, Page())))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("definition");
        ((Action)(() => sut.Build(definition, null!, Page())))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("plan");
        ((Action)(() => sut.Build(definition, plan, null!)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("page");

        var nonPivotPlan = Plan(definition, new ReportLayoutDto(
            Measures: [new ReportMeasureSelectionDto("debit_amount")]));
        ((Action)(() => sut.Build(definition, nonPivotPlan, Page())))
            .Should().Throw<NgbInvariantViolationException>()
            .WithMessage("*requires at least one column group*");
    }

    [Theory]
    [InlineData("columns", "layout.columnGroups")]
    [InlineData("rows-with-groups", "layout.rowGroups")]
    [InlineData("rows-columns-only", "layout.columnGroups")]
    [InlineData("cells", "layout.columnGroups")]
    public void Build_EnforcesEveryIndependentRenderingCap(string cap, string fieldPath)
    {
        var baseDefinition = Definition();
        var hasRows = cap == "rows-with-groups";
        var plan = Plan(baseDefinition, new ReportLayoutDto(
            RowGroups: hasRows ? [new ReportGroupingDto("account_display")] : [],
            ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
            Measures: [new ReportMeasureSelectionDto("debit_amount")],
            ShowGrandTotals: true));
        var capabilities = baseDefinition.Capabilities with
        {
            MaxVisibleColumns = cap == "columns" ? 1 : 100,
            MaxVisibleRows = cap.StartsWith("rows", StringComparison.Ordinal) ? 3 : 100,
            MaxRenderedCells = cap == "cells" ? 7 : 10_000
        };
        var definition = new ReportDefinitionRuntimeModel(baseDefinition.Definition with { Capabilities = capabilities });
        var sut = Builder(plan, definition);
        var row = hasRows
            ? DataRow(("account_display", "Account"), ("period_utc__month", new DateOnly(2026, 1, 1)), ("debit_amount__sum", 1m))
            : DataRow(("period_utc__month", new DateOnly(2026, 1, 1)), ("debit_amount__sum", 1m));

        var exception = ((Action)(() => sut.Build(definition, plan, Page(row))))
            .Should().Throw<ReportLayoutValidationException>().Which;

        exception.Context["fieldPath"].Should().Be(fieldPath);
    }

    [Fact]
    public void Build_WithoutRowAxisOrMeasures_UsesAllTupleAndProducesAValueLessDetailRow()
    {
        var definition = Definition();
        var plan = Plan(definition, new ReportLayoutDto(
            ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)]));
        var result = Builder(plan, definition).Build(
            definition,
            plan,
            Page(DataRow(("period_utc__month", (object?)null))));

        result.Columns.Should().BeEmpty();
        result.Rows.Should().ContainSingle().Which.Cells.Should().BeEmpty();
        result.Diagnostics["pivotColumnLeafCount"].Should().Be("1");

        var groupedPlan = Plan(definition, new ReportLayoutDto(
            RowGroups: [new ReportGroupingDto("account_display")],
            ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
            ShowGrandTotals: true));
        var grouped = Builder(groupedPlan, definition).Build(
            definition,
            groupedPlan,
            Page(DataRow(("account_display", "Account"), ("period_utc__month", new DateOnly(2026, 1, 1)))));
        grouped.Rows.Should().ContainSingle().Which.RowKind.Should().Be(ReportRowKind.Group);
    }

    [Fact]
    public void Build_GroupedDetailsMultipleMeasures_CoversLeafBlankInlineSubtotalAndGrandTotalCells()
    {
        var definition = Definition();
        var detailedPlan = Plan(definition, new ReportLayoutDto(
            RowGroups:
            [
                new ReportGroupingDto("account_display"),
                new ReportGroupingDto("period_utc", ReportTimeGrain.Month)
            ],
            ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Year)],
            DetailFields: ["document_display"],
            Measures:
            [
                new ReportMeasureSelectionDto("debit_amount"),
                new ReportMeasureSelectionDto("credit_amount")
            ],
            ShowDetails: true,
            ShowSubtotals: true,
            ShowSubtotalsOnSeparateRows: true,
            ShowGrandTotals: true));
        var rows = new[]
        {
            DataRow(
                ("account_display", "Account"),
                ("period_utc__month", new DateOnly(2026, 1, 1)),
                ("period_utc__year", new DateOnly(2026, 1, 1)),
                ("document_display", "D-1"),
                ("debit_amount__sum", 10m),
                ("credit_amount__sum", 2m)),
            DataRow(
                ("account_display", "Account"),
                ("period_utc__month", new DateOnly(2026, 2, 1)),
                ("period_utc__year", new DateOnly(2026, 1, 1)),
                ("document_display", "D-2"),
                ("debit_amount__sum", 5m),
                ("credit_amount__sum", 1m))
        };

        var detailed = Builder(detailedPlan, definition).Build(definition, detailedPlan, Page(rows));

        detailed.Columns.Should().HaveCount(6);
        detailed.Rows.Select(x => x.RowKind).Should().Contain([
            ReportRowKind.Group,
            ReportRowKind.Detail,
            ReportRowKind.Subtotal,
            ReportRowKind.Total
        ]);
        detailed.Rows.Where(x => x.RowKind == ReportRowKind.Detail)
            .Should().OnlyContain(x => x.Cells[0].Display == null && x.OutlineLevel == 2);
        detailed.Rows.Single(x => x.RowKind == ReportRowKind.Total).Cells[1].Display.Should().BeNull();
        detailed.Columns.Where(x => x.SemanticRole == "pivot-measure").Select(x => x.Title)
            .Should().Equal("Debit", "Credit");

        var inlinePlan = Plan(definition, new ReportLayoutDto(
            RowGroups: [new ReportGroupingDto("account_display")],
            ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Year)],
            Measures: [new ReportMeasureSelectionDto("debit_amount")],
            ShowSubtotals: false));
        var inline = Builder(inlinePlan, definition).Build(definition, inlinePlan, Page(rows[0]));
        inline.Rows[0].RowKind.Should().Be(ReportRowKind.Group);
        inline.Rows[0].Cells[1].Display.Should().Be("10");

        var detailsWithoutSubtotalsPlan = Plan(definition, new ReportLayoutDto(
            RowGroups: [new ReportGroupingDto("account_display")],
            ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Year)],
            DetailFields: ["document_display"],
            Measures: [new ReportMeasureSelectionDto("debit_amount")],
            ShowDetails: true,
            ShowSubtotals: false));
        var detailsWithoutSubtotals = Builder(detailsWithoutSubtotalsPlan, definition)
            .Build(definition, detailsWithoutSubtotalsPlan, Page(rows[0]));
        detailsWithoutSubtotals.Rows[0].Cells[1].Display.Should().BeNull();

        var noTotalsPlan = Plan(definition, new ReportLayoutDto(
            ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Year)],
            Measures: [new ReportMeasureSelectionDto("debit_amount")],
            ShowGrandTotals: false));
        var noTotals = Builder(noTotalsPlan, definition).Build(definition, noTotalsPlan, Page(rows[0]));
        noTotals.Columns.Should().OnlyContain(x => x.SemanticRole == "pivot-measure");
        noTotals.Rows.Should().OnlyContain(x => x.RowKind != ReportRowKind.Total);
    }

    [Fact]
    public void NumericHelper_HandlesNullAndEverySupportedInt64AndDecimalRepresentation()
    {
        var intMeasure = Measure("int64");
        var decimalMeasure = Measure("decimal");
        ReportPivotMatrixBuilderHelper.AddValues(intMeasure, 5L, null).Should().Be(5L);
        ReportPivotMatrixBuilderHelper.AddValues(decimalMeasure, 5m, null).Should().Be(5m);

        object?[] values = [null, 1L, 1, (short)1, (byte)1, 1m, 1d, 1f, "1"];
        foreach (var value in values)
        {
            ReportPivotMatrixBuilderHelper.AddValues(intMeasure, value, 1L).Should().Be(1L + Convert.ToInt64(value));
            ReportPivotMatrixBuilderHelper.AddValues(intMeasure, 1L, value ?? 0L).Should().Be(1L + Convert.ToInt64(value ?? 0L));
            ReportPivotMatrixBuilderHelper.AddValues(decimalMeasure, value, 1m).Should().Be(1m + Convert.ToDecimal(value));
            ReportPivotMatrixBuilderHelper.AddValues(decimalMeasure, 1m, value ?? 0m).Should().Be(1m + Convert.ToDecimal(value ?? 0m));
        }
    }

    [Fact]
    public void PivotLeafRow_AggregatesMatchingMeasureValuesAndIgnoresNullsAndOtherMeasures()
    {
        var amount = Measure("decimal");
        var other = amount with { MeasureCode = "other", OutputCode = "other", Label = "Other" };
        var row = new PivotLeafRow(
            "row",
            new Dictionary<string, object?>(),
            [],
            new Dictionary<string, object?>());

        row.AddValue("jan", amount, null);
        row.AddValue("jan", amount, 2m);
        row.AddValue("feb", amount, 3m);
        row.AddValue("jan", other, 100m);

        row.GetValue("jan", amount.OutputCode).Should().Be(2m);
        row.GetValue("missing", amount.OutputCode).Should().BeNull();
        row.GetTotal(amount).Should().Be(5m);
        row.GetTotal(amount with { OutputCode = "AMOUNT" }).Should().Be(5m);
    }

    private static ReportPivotMatrixBuilder Builder(ReportQueryPlan plan, ReportDefinitionRuntimeModel definition)
    {
        var formatter = new ReportCellFormatter();
        var resolver = new ReportComposableCellActionResolver(plan, definition.Dataset);
        return new ReportPivotMatrixBuilder(formatter, new ReportPivotHeaderBuilder(formatter, resolver), resolver);
    }

    private static ReportDefinitionRuntimeModel Definition()
        => new(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());

    private static ReportQueryPlan Plan(ReportDefinitionRuntimeModel definition, ReportLayoutDto layout)
    {
        var request = new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string>
            {
                ["from_utc"] = "2026-01-01",
                ["to_utc"] = "2026-12-31"
            },
            Layout: layout,
            Limit: 50);
        return new ReportExecutionPlanner().BuildPlan(new ReportExecutionContext(definition, request, layout));
    }

    private static ReportDataPage Page(params ReportDataRow[] rows)
        => new([], rows, 0, 50, rows.Length, false);

    private static ReportDataRow DataRow(params (string Code, object? Value)[] values)
        => new(values.ToDictionary(x => x.Code, x => x.Value, StringComparer.OrdinalIgnoreCase));

    private static PivotPlanMeasure Measure(string dataType)
        => new("amount", "amount", "Amount", dataType, ReportAggregationKind.Sum);
}
