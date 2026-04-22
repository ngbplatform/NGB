using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Definitions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportSheetBuilder_P0Tests
{
    [Fact]
    public void BuildSheet_GroupedRows_Inline_Totals_On_Group_Rows_And_Keep_GrandTotal()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups:
                [
                    new ReportGroupingDto("account_display"),
                    new ReportGroupingDto("document_display")
                ],
                Measures:
                [
                    new ReportMeasureSelectionDto("debit_amount")
                ],
                ShowDetails: false,
                ShowSubtotals: true,
                ShowSubtotalsOnSeparateRows: false,
                ShowGrandTotals: true));

        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("document_display", "Document", "string", "row-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-001",
                    ["debit_amount__sum"] = 100m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-002",
                    ["debit_amount__sum"] = 25m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1200 — Cash",
                    ["document_display"] = "RC-003",
                    ["debit_amount__sum"] = 10m
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 3,
            HasMore: false);

        var sheet = new ReportSheetBuilder().BuildSheet(definition, plan, page);

        sheet.Meta!.HasRowOutline.Should().BeTrue();
        sheet.Columns.Select(x => x.Code).Should().Equal(ReportSheetBuilder.RowHierarchyColumnCode, "debit_amount__sum");
        sheet.Columns[0].Title.Should().Be("Account\nDocument");
        sheet.Rows.Select(x => x.RowKind).Should().Equal(
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Total);

        sheet.Rows[0].OutlineLevel.Should().Be(0);
        sheet.Rows[1].OutlineLevel.Should().Be(1);
        sheet.Rows[2].OutlineLevel.Should().Be(1);
        sheet.Rows[3].OutlineLevel.Should().Be(0);
        sheet.Rows[0].Cells[0].Display.Should().Be("1100 — Accounts Receivable");
        sheet.Rows[0].Cells[1].Display.Should().Be("125");
        sheet.Rows[1].Cells[0].Display.Should().Be("RC-001");
        sheet.Rows[1].Cells[1].Display.Should().Be("100");
        sheet.Rows[3].Cells[0].Display.Should().Be("1200 — Cash");
        sheet.Rows[3].Cells[1].Display.Should().Be("10");
        sheet.Rows.Should().NotContain(x => x.RowKind == ReportRowKind.Subtotal);
        sheet.Rows[^1].Cells[0].Display.Should().Be("Total");
        sheet.Rows[^1].Cells[1].Display.Should().Be("135");
    }

    [Fact]
    public void BuildSheet_GroupedRows_When_ShowSubtotalsOnSeparateRows_Is_True_Emits_Subtotal_Rows_Instead_Of_Inline_Group_Totals()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups:
                [
                    new ReportGroupingDto("account_display"),
                    new ReportGroupingDto("document_display")
                ],
                Measures:
                [
                    new ReportMeasureSelectionDto("debit_amount")
                ],
                ShowDetails: false,
                ShowSubtotals: true,
                ShowSubtotalsOnSeparateRows: true,
                ShowGrandTotals: true));

        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("document_display", "Document", "string", "row-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-001",
                    ["debit_amount__sum"] = 100m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-002",
                    ["debit_amount__sum"] = 25m
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 2,
            HasMore: false);

        var sheet = new ReportSheetBuilder().BuildSheet(definition, plan, page);

        sheet.Rows.Select(x => x.RowKind).Should().Equal(
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Subtotal,
            ReportRowKind.Total);

        sheet.Rows[0].Cells[0].Display.Should().Be("1100 — Accounts Receivable");
        sheet.Rows[0].Cells[1].Display.Should().BeNullOrEmpty();
        sheet.Rows[1].Cells[0].Display.Should().Be("RC-001");
        sheet.Rows[1].Cells[1].Display.Should().Be("100");
        sheet.Rows[2].Cells[0].Display.Should().Be("RC-002");
        sheet.Rows[2].Cells[1].Display.Should().Be("25");
        sheet.Rows[3].Cells[0].Display.Should().Be("1100 — Accounts Receivable subtotal");
        sheet.Rows[3].Cells[1].Display.Should().Be("125");
        sheet.Rows.Should().NotContain(x => x.RowKind == ReportRowKind.Subtotal && x.OutlineLevel == 1);
    }

    [Fact]
    public void BuildSheet_DetailMode_Emits_DetailRows_Under_Group_With_Inline_Group_Totals()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("account_display")],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                DetailFields: ["document_display"],
                ShowDetails: true,
                ShowSubtotals: true,
                ShowSubtotalsOnSeparateRows: false,
                ShowGrandTotals: true));

        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("document_display", "Document", "string", "detail"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-001",
                    ["debit_amount__sum"] = 100m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-002",
                    ["debit_amount__sum"] = 25m
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 2,
            HasMore: false);

        var sheet = new ReportSheetBuilder().BuildSheet(definition, plan, page);

        sheet.Columns.Select(x => x.Code).Should().Equal(ReportSheetBuilder.RowHierarchyColumnCode, "document_display", "debit_amount__sum");
        sheet.Columns[0].Title.Should().Be("Account");
        sheet.Rows.Select(x => x.RowKind).Should().Equal(
            ReportRowKind.Group,
            ReportRowKind.Detail,
            ReportRowKind.Detail,
            ReportRowKind.Total);

        sheet.Rows[0].Cells[0].Display.Should().Be("1100 — Accounts Receivable");
        sheet.Rows[0].Cells[2].Display.Should().Be("125");
        sheet.Rows[1].OutlineLevel.Should().Be(1);
        sheet.Rows[1].Cells[0].Display.Should().BeNullOrEmpty();
        sheet.Rows[1].Cells[1].Display.Should().Be("RC-001");
        sheet.Rows[1].Cells[2].Display.Should().Be("100");
        sheet.Rows.Should().NotContain(x => x.RowKind == ReportRowKind.Subtotal);
    }

    [Fact]
    public void BuildSheet_NoRows_Returns_Empty_Sheet()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("account_display")],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowSubtotals: true,
                ShowGrandTotals: true));

        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
            ],
            Rows: [],
            Offset: 0,
            Limit: 50,
            Total: 0,
            HasMore: false);

        var sheet = new ReportSheetBuilder().BuildSheet(definition, plan, page);

        sheet.Rows.Should().BeEmpty();
        sheet.Meta!.HasRowOutline.Should().BeTrue();
        sheet.Meta.Diagnostics!["state"].Should().Be("empty");
    }

    [Fact]
    public void BuildSheet_PivotByMonth_Renders_Matrix_Headers_And_GrandTotals()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("account_display")],
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowSubtotals: true,
                ShowSubtotalsOnSeparateRows: false,
                ShowGrandTotals: true));

        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("period_utc__month", "Period", "datetime", "column-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["period_utc__month"] = new DateOnly(2026, 1, 1),
                    ["debit_amount__sum"] = 100m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["period_utc__month"] = new DateOnly(2026, 2, 1),
                    ["debit_amount__sum"] = 25m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1200 — Cash",
                    ["period_utc__month"] = new DateOnly(2026, 1, 1),
                    ["debit_amount__sum"] = 10m
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 3,
            HasMore: false);

        var sheet = new ReportSheetBuilder().BuildSheet(definition, plan, page);

        sheet.Meta!.IsPivot.Should().BeTrue();
        sheet.Meta.HasColumnGroups.Should().BeTrue();
        sheet.HeaderRows.Should().NotBeNull();
        sheet.HeaderRows!.Should().HaveCount(2);
        sheet.Columns.Should().HaveCount(4);
        sheet.Columns.Select(x => x.Code).Should().Equal(ReportSheetBuilder.RowHierarchyColumnCode, "pivot_0_debit_amount__sum", "pivot_1_debit_amount__sum", "total_debit_amount__sum");
        sheet.HeaderRows[0].Cells[0].Display.Should().Be("Account");
        sheet.HeaderRows[0].Cells[1].Display.Should().Be("January 2026");
        sheet.HeaderRows[0].Cells[2].Display.Should().Be("February 2026");
        sheet.HeaderRows[0].Cells[3].Display.Should().Be("Total");
        sheet.HeaderRows[1].Cells.Select(x => x.Display).Should().Equal("Debit", "Debit", "Debit");
        sheet.Rows.Select(x => x.RowKind).Should().Equal(ReportRowKind.Group, ReportRowKind.Group, ReportRowKind.Total);
        sheet.Rows[0].Cells[0].Display.Should().Be("1100 — Accounts Receivable");
        sheet.Rows[0].Cells[1].Display.Should().Be("100");
        sheet.Rows[0].Cells[2].Display.Should().Be("25");
        sheet.Rows[0].Cells[3].Display.Should().Be("125");
        sheet.Rows[1].Cells[0].Display.Should().Be("1200 — Cash");
        sheet.Rows[1].Cells[1].Display.Should().Be("10");
        sheet.Rows[1].Cells[2].Display.Should().BeNullOrEmpty();
        sheet.Rows[1].Cells[3].Display.Should().Be("10");
        sheet.Rows[^1].Cells[1].Display.Should().Be("110");
        sheet.Rows[^1].Cells[2].Display.Should().Be("25");
        sheet.Rows[^1].Cells[3].Display.Should().Be("135");
    }

    [Fact]
    public void BuildSheet_Pivot_When_ShowSubtotalsOnSeparateRows_Is_True_Emits_Subtotal_Rows()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups:
                [
                    new ReportGroupingDto("account_display"),
                    new ReportGroupingDto("document_display")
                ],
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowSubtotals: true,
                ShowSubtotalsOnSeparateRows: true,
                ShowGrandTotals: true));

        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("document_display", "Document", "string", "row-group"),
                new ReportDataColumn("period_utc__month", "Period", "datetime", "column-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-001",
                    ["period_utc__month"] = new DateOnly(2026, 1, 1),
                    ["debit_amount__sum"] = 100m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-002",
                    ["period_utc__month"] = new DateOnly(2026, 1, 1),
                    ["debit_amount__sum"] = 25m
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 2,
            HasMore: false);

        var sheet = new ReportSheetBuilder().BuildSheet(definition, plan, page);

        sheet.Rows.Select(x => x.RowKind).Should().Equal(
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Subtotal,
            ReportRowKind.Total);

        sheet.Rows[0].Cells[0].Display.Should().Be("1100 — Accounts Receivable");
        sheet.Rows[0].Cells[1].Display.Should().BeNullOrEmpty();
        sheet.Rows[0].Cells[2].Display.Should().BeNullOrEmpty();
        sheet.Rows[1].Cells[0].Display.Should().Be("RC-001");
        sheet.Rows[1].Cells[1].Display.Should().Be("100");
        sheet.Rows[1].Cells[2].Display.Should().Be("100");
        sheet.Rows[2].Cells[0].Display.Should().Be("RC-002");
        sheet.Rows[2].Cells[1].Display.Should().Be("25");
        sheet.Rows[2].Cells[2].Display.Should().Be("25");
        sheet.Rows[3].Cells[0].Display.Should().Be("1100 — Accounts Receivable subtotal");
        sheet.Rows[3].Cells[1].Display.Should().Be("125");
        sheet.Rows[3].Cells[2].Display.Should().Be("125");
        sheet.Rows.Should().NotContain(x => x.RowKind == ReportRowKind.Subtotal && x.OutlineLevel == 1);
    }

    [Fact]
    public void BuildSheet_GroupedRows_With_Month_TimeGrain_Formats_Period_Group_Row_In_UserFacing_Display()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups:
                [
                    new ReportGroupingDto("account_display"),
                    new ReportGroupingDto("period_utc", ReportTimeGrain.Month)
                ],
                Measures:
                [
                    new ReportMeasureSelectionDto("debit_amount")
                ],
                ShowDetails: false,
                ShowSubtotals: true,
                ShowSubtotalsOnSeparateRows: false,
                ShowGrandTotals: true));

        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("period_utc__month", "Period", "datetime", "row-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["period_utc__month"] = new DateOnly(2026, 3, 1),
                    ["debit_amount__sum"] = 100m
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 1,
            HasMore: false);

        var sheet = new ReportSheetBuilder().BuildSheet(definition, plan, page);

        sheet.Columns.Select(x => x.Code).Should().Equal(ReportSheetBuilder.RowHierarchyColumnCode, "debit_amount__sum");
        sheet.Columns[0].Title.Should().Be("Account\nPeriod");
        sheet.Rows.Select(x => x.RowKind).Should().Equal(
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Total);
        sheet.Rows[0].Cells[0].Display.Should().Be("1100 — Accounts Receivable");
        sheet.Rows[0].Cells[1].Display.Should().Be("100");
        sheet.Rows[1].Cells[0].Display.Should().Be("March 2026");
        sheet.Rows[1].Cells[1].Display.Should().Be("100");
    }

    [Fact]
    public void BuildSheet_PivotWithTwoRowGroups_Inline_Totals_On_Group_Rows_Using_Single_Hierarchy_Column()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups:
                [
                    new ReportGroupingDto("account_display"),
                    new ReportGroupingDto("document_display")
                ],
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowSubtotals: true,
                ShowSubtotalsOnSeparateRows: false,
                ShowGrandTotals: true));

        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("document_display", "Document", "string", "row-group"),
                new ReportDataColumn("period_utc__month", "Period", "datetime", "column-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-001",
                    ["period_utc__month"] = new DateOnly(2026, 1, 1),
                    ["debit_amount__sum"] = 100m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-002",
                    ["period_utc__month"] = new DateOnly(2026, 1, 1),
                    ["debit_amount__sum"] = 25m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1200 — Cash",
                    ["document_display"] = "RC-003",
                    ["period_utc__month"] = new DateOnly(2026, 1, 1),
                    ["debit_amount__sum"] = 10m
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 3,
            HasMore: false);

        var sheet = new ReportSheetBuilder().BuildSheet(definition, plan, page);

        sheet.Columns.Select(x => x.Code).Should().Equal(ReportSheetBuilder.RowHierarchyColumnCode, "pivot_0_debit_amount__sum", "total_debit_amount__sum");
        sheet.HeaderRows.Should().NotBeNull();
        sheet.HeaderRows![0].Cells[0].Display.Should().Be("Account\nDocument");
        sheet.Rows.Select(x => x.RowKind).Should().Equal(
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Group,
            ReportRowKind.Total);
        sheet.Rows[0].OutlineLevel.Should().Be(0);
        sheet.Rows[1].OutlineLevel.Should().Be(1);
        sheet.Rows[0].Cells[0].Display.Should().Be("1100 — Accounts Receivable");
        sheet.Rows[0].Cells[1].Display.Should().Be("125");
        sheet.Rows[0].Cells[2].Display.Should().Be("125");
        sheet.Rows[1].Cells[0].Display.Should().Be("RC-001");
        sheet.Rows[1].Cells[1].Display.Should().Be("100");
        sheet.Rows[1].Cells[2].Display.Should().Be("100");
        sheet.Rows[3].Cells[0].Display.Should().Be("1200 — Cash");
        sheet.Rows[3].Cells[1].Display.Should().Be("10");
        sheet.Rows[3].Cells[2].Display.Should().Be("10");
        sheet.Rows.Should().NotContain(x => x.RowKind == ReportRowKind.Subtotal);
        sheet.Rows[^1].Cells[1].Display.Should().Be("135");
        sheet.Rows[^1].Cells[2].Display.Should().Be("135");
    }

    [Fact]
    public void BuildSheet_Pivot_When_MaxVisibleColumns_Exceeded_Throws_Validation()
    {
        var rawDefinition = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();
        var definition = new ReportDefinitionRuntimeModel(rawDefinition with
        {
            Capabilities = rawDefinition.Capabilities! with
            {
                MaxVisibleColumns = 3,
                AllowsColumnGroups = true,
                MaxColumnGroupDepth = 2
            }
        });
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("account_display")],
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowGrandTotals: true));

        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("period_utc__month", "Period", "datetime", "column-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["period_utc__month"] = new DateOnly(2026, 1, 1),
                    ["debit_amount__sum"] = 10m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["period_utc__month"] = new DateOnly(2026, 2, 1),
                    ["debit_amount__sum"] = 20m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["period_utc__month"] = new DateOnly(2026, 3, 1),
                    ["debit_amount__sum"] = 30m
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 3,
            HasMore: false);

        var act = () => new ReportSheetBuilder().BuildSheet(definition, plan, page);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*display 5 columns*limit of 3*");
    }

    [Fact]
    public void BuildSheet_GroupedRows_When_MaxVisibleRows_Exceeded_Throws_Validation()
    {
        var rawDefinition = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();
        var definition = new ReportDefinitionRuntimeModel(rawDefinition with
        {
            Capabilities = rawDefinition.Capabilities! with
            {
                MaxVisibleRows = 3
            }
        });
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups:
                [
                    new ReportGroupingDto("account_display"),
                    new ReportGroupingDto("document_display")
                ],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowDetails: false,
                ShowSubtotals: true,
                ShowSubtotalsOnSeparateRows: false,
                ShowGrandTotals: true));

        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("document_display", "Document", "string", "row-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-001",
                    ["debit_amount__sum"] = 100m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "RC-002",
                    ["debit_amount__sum"] = 25m
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 2,
            HasMore: false);

        var act = () => new ReportSheetBuilder().BuildSheet(definition, plan, page);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*display 4 rows*limit of 3*");
    }

    [Fact]
    public void BuildSheet_Pivot_When_MaxVisibleRows_Exceeded_Throws_Validation()
    {
        var rawDefinition = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();
        var definition = new ReportDefinitionRuntimeModel(rawDefinition with
        {
            Capabilities = rawDefinition.Capabilities! with
            {
                MaxVisibleRows = 4,
                AllowsColumnGroups = true,
                MaxColumnGroupDepth = 2
            }
        });
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("account_display")],
                ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowGrandTotals: true));

        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("period_utc__month", "Period", "datetime", "column-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["period_utc__month"] = new DateOnly(2026, 1, 1),
                    ["debit_amount__sum"] = 10m
                }),
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1200 — Cash",
                    ["period_utc__month"] = new DateOnly(2026, 2, 1),
                    ["debit_amount__sum"] = 20m
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 2,
            HasMore: false);

        var act = () => new ReportSheetBuilder().BuildSheet(definition, plan, page);

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*display 5 rows*limit of 4*");
    }

    private static ReportQueryPlan BuildPlan(ReportDefinitionRuntimeModel definition, ReportLayoutDto layout)
    {
        var request = new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["from_utc"] = "2026-03-01",
                ["to_utc"] = "2026-03-31"
            },
            Layout: layout,
            Offset: 0,
            Limit: 50);

        var context = new ReportExecutionContext(definition, request, layout);
        return new ReportExecutionPlanner().BuildPlan(context);
    }

    [Fact]
    public void BuildSheet_GroupRows_Attach_Click_Actions_For_Account_And_Document()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups:
                [
                    new ReportGroupingDto("account_display"),
                    new ReportGroupingDto("document_display")
                ],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowDetails: false,
                ShowSubtotals: true,
                ShowGrandTotals: true));

        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var documentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("account_display", "Account", "string", "row-group"),
                new ReportDataColumn("document_display", "Document", "string", "row-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure"),
                new ReportDataColumn(ReportInteractiveSupport.SupportAccountId, ReportInteractiveSupport.SupportAccountId, "uuid", "support"),
                new ReportDataColumn(ReportInteractiveSupport.SupportDocumentId, ReportInteractiveSupport.SupportDocumentId, "uuid", "support"),
                new ReportDataColumn(ReportInteractiveSupport.SupportDocumentType, ReportInteractiveSupport.SupportDocumentType, "string", "support")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "Receivable Charge RC-2026-000001 2026-03-05",
                    ["debit_amount__sum"] = 100m,
                    [ReportInteractiveSupport.SupportAccountId] = accountId,
                    [ReportInteractiveSupport.SupportDocumentId] = documentId,
                    [ReportInteractiveSupport.SupportDocumentType] = "pm.receivable_charge"
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 1,
            HasMore: false);

        var sheet = new ReportSheetBuilder().BuildSheet(definition, plan, page);

        sheet.Rows[0].Cells[0].Action.Should().NotBeNull();
        sheet.Rows[0].Cells[0].Action!.Kind.Should().Be(ReportCellActionKinds.OpenReport);
        sheet.Rows[0].Cells[0].Action!.Report!.ReportCode.Should().Be("accounting.account_card");
        sheet.Rows[0].Cells[0].Action!.Report!.Parameters!["from_utc"].Should().Be("2026-03-01");
        sheet.Rows[0].Cells[0].Action!.Report!.Parameters!["to_utc"].Should().Be("2026-03-31");
        sheet.Rows[0].Cells[0].Action!.Report!.Filters!["account_id"].Value.GetGuid().Should().Be(accountId);

        sheet.Rows[1].Cells[0].Action.Should().BeEquivalentTo(new ReportCellActionDto(
            "open_document",
            DocumentType: "pm.receivable_charge",
            DocumentId: documentId));
    }

    [Fact]
    public void BuildSheet_PivotColumnHeaders_Attach_Click_Actions_For_Account_And_Document()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                ColumnGroups:
                [
                    new ReportGroupingDto("account_display"),
                    new ReportGroupingDto("document_display")
                ],
                Measures: [new ReportMeasureSelectionDto("debit_amount")],
                ShowDetails: false,
                ShowSubtotals: true,
                ShowGrandTotals: true));

        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var documentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("period_utc__month", "Period", "datetime", "row-group"),
                new ReportDataColumn("account_display", "Account", "string", "column-group"),
                new ReportDataColumn("document_display", "Document", "string", "column-group"),
                new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure"),
                new ReportDataColumn("account_id", "account_id", "uuid", "support"),
                new ReportDataColumn("document_id", "document_id", "uuid", "support"),
                new ReportDataColumn(ReportInteractiveSupport.SupportDocumentType, ReportInteractiveSupport.SupportDocumentType, "string", "support")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["period_utc__month"] = new DateOnly(2026, 3, 1),
                    ["account_display"] = "1100 — Accounts Receivable",
                    ["document_display"] = "Receivable Charge RC-2026-000001 2026-03-05",
                    ["debit_amount__sum"] = 100m,
                    ["account_id"] = accountId,
                    ["document_id"] = documentId,
                    [ReportInteractiveSupport.SupportDocumentType] = "pm.receivable_charge"
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 1,
            HasMore: false);

        var sheet = new ReportSheetBuilder().BuildSheet(definition, plan, page);

        sheet.HeaderRows.Should().NotBeNull();

        var accountHeaderCell = sheet.HeaderRows!
            .SelectMany(static row => row.Cells)
            .Single(cell => cell.Action?.Kind == ReportCellActionKinds.OpenReport
                && string.Equals(cell.Action.Report?.ReportCode, "accounting.account_card", StringComparison.Ordinal));

        accountHeaderCell.Display.Should().Be("1100 — Accounts Receivable");
        accountHeaderCell.Action!.Report!.Filters!["account_id"].Value.GetGuid().Should().Be(accountId);

        var documentHeaderCell = sheet.HeaderRows
            .SelectMany(static row => row.Cells)
            .Single(cell => cell.Action?.Kind == ReportCellActionKinds.OpenDocument);

        documentHeaderCell.Display.Should().Be("Receivable Charge RC-2026-000001 2026-03-05");
        documentHeaderCell.Action.Should().BeEquivalentTo(new ReportCellActionDto(
            "open_document",
            DocumentType: "pm.receivable_charge",
            DocumentId: documentId));
    }

    [Fact]
    public void BuildSheet_GroupRows_Attach_Click_Actions_For_Catalog_Display_Fields()
    {
        var definition = new ReportDefinitionRuntimeModel(new ReportDefinitionDto(
            ReportCode: "inventory.catalog-links",
            Name: "Inventory Catalog Links",
            Mode: ReportExecutionMode.Composable,
            Dataset: new ReportDatasetDto(
                DatasetCode: "inventory.catalog-links",
                Fields:
                [
                    new ReportFieldDto(
                        "warehouse_id",
                        "Warehouse",
                        "uuid",
                        ReportFieldKind.Dimension,
                        Lookup: new CatalogLookupSourceDto("trade.warehouse")),
                    new ReportFieldDto(
                        "warehouse_display",
                        "Warehouse",
                        "string",
                        ReportFieldKind.Dimension,
                        IsGroupable: true,
                        IsSortable: true,
                        IsSelectable: true),
                    new ReportFieldDto(
                        "item_id",
                        "Item",
                        "uuid",
                        ReportFieldKind.Dimension,
                        Lookup: new CatalogLookupSourceDto("trade.item")),
                    new ReportFieldDto(
                        "item_display",
                        "Item",
                        "string",
                        ReportFieldKind.Dimension,
                        IsGroupable: true,
                        IsSortable: true,
                        IsSelectable: true)
                ],
                Measures:
                [
                    new ReportMeasureDto(
                        "quantity_on_hand",
                        "Quantity On Hand",
                        "decimal",
                        [ReportAggregationKind.Sum])
                ])));

        var plan = BuildPlan(
            definition,
            new ReportLayoutDto(
                RowGroups:
                [
                    new ReportGroupingDto("warehouse_display"),
                    new ReportGroupingDto("item_display")
                ],
                Measures:
                [
                    new ReportMeasureSelectionDto("quantity_on_hand")
                ],
                ShowDetails: false,
                ShowSubtotals: true,
                ShowGrandTotals: true));

        var warehouseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var itemId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var page = new ReportDataPage(
            Columns:
            [
                new ReportDataColumn("warehouse_display", "Warehouse", "string", "row-group"),
                new ReportDataColumn("item_display", "Item", "string", "row-group"),
                new ReportDataColumn("quantity_on_hand__sum", "Quantity On Hand", "decimal", "measure"),
                new ReportDataColumn("warehouse_id", "warehouse_id", "uuid", "support"),
                new ReportDataColumn("item_id", "item_id", "uuid", "support")
            ],
            Rows:
            [
                new ReportDataRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["warehouse_display"] = "Main Warehouse",
                    ["item_display"] = "Widget Alpha",
                    ["quantity_on_hand__sum"] = 6m,
                    ["warehouse_id"] = warehouseId,
                    ["item_id"] = itemId
                })
            ],
            Offset: 0,
            Limit: 50,
            Total: 1,
            HasMore: false);

        var sheet = new ReportSheetBuilder().BuildSheet(definition, plan, page);

        sheet.Rows[0].Cells[0].Action.Should().BeEquivalentTo(new ReportCellActionDto(
            ReportCellActionKinds.OpenCatalog,
            CatalogType: "trade.warehouse",
            CatalogId: warehouseId));

        sheet.Rows[1].Cells[0].Action.Should().BeEquivalentTo(new ReportCellActionDto(
            ReportCellActionKinds.OpenCatalog,
            CatalogType: "trade.item",
            CatalogId: itemId));
    }
}
