using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportPagedQueryExecutorTests
{
    [Fact]
    public async Task Oversized_row_keys_are_rejected_before_reading_cells()
    {
        var source = new Source();
        var plan = Plan(true) with { RowGroups = [], DetailFields = Enumerable.Range(0, ReportRowSelectionLimits.MaxFields + 1).Select(i => new NGB.Runtime.Reporting.Planning.ReportPlanFieldSelection("detail", "d" + i, "Detail", "string")).ToArray() };
        var action = () => Executor(source).ExecuteAsync(Definition(), plan, new(), default);
        await action.Should().ThrowAsync<NgbArgumentInvalidException>().WithMessage("*key*");
        source.Calls.Should().ContainSingle();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task Structured_group_keys_are_rejected_before_reading(string json)
    {
        var source = new Mock<IReportPageDataSource>(MockBehavior.Strict);
        var action = () => Executor(source.Object).ExecuteAsync(Definition(), Plan(), new(GroupPath: [JsonDocument.Parse(json).RootElement]), default);
        await action.Should().ThrowAsync<NgbArgumentInvalidException>().WithMessage("*group path*");
        source.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("text", "null", true)]
    [InlineData("text", "false", false)]
    [InlineData("uuid", "false", false)]
    [InlineData("bool", "false", true)]
    [InlineData("int", "1", true)]
    [InlineData("int32", "1", true)]
    [InlineData("integer", "1", true)]
    [InlineData("long", "1", true)]
    [InlineData("number", "1", true)]
    [InlineData("double", "1", true)]
    [InlineData("float", "1", true)]
    [InlineData("datetime", "null", true)]
    [InlineData("datetime", "false", false)]
    [InlineData("datetimeoffset", "false", false)]
    [InlineData("string", "\"a\"", true)]
    [InlineData("string", "1", false)]
    [InlineData("guid", "\"01234567-89ab-cdef-0123-456789abcdef\"", true)]
    [InlineData("guid", "\"bad\"", false)]
    [InlineData("guid", "1", false)]
    [InlineData("boolean", "true", true)]
    [InlineData("boolean", "false", true)]
    [InlineData("boolean", "1", false)]
    [InlineData("int64", "1", true)]
    [InlineData("int64", "1.5", false)]
    [InlineData("int64", "\"1\"", false)]
    [InlineData("decimal", "1.5", true)]
    [InlineData("decimal", "\"1\"", false)]
    [InlineData("date", "\"2026-09-22\"", true)]
    [InlineData("date", "1", false)]
    [InlineData("custom", "1", true)]
    [InlineData("string", "null", true)]
    public async Task Group_keys_are_type_checked_and_forwarded_as_predicates(string type, string json, bool valid)
    {
        var source = new Source();
        var plan = Plan() with { RowGroups = [new("group", "group", "Group", type, false)] };
        var action = () => Executor(source).ExecuteAsync(Definition(), plan, new(GroupPath: [JsonDocument.Parse(json).RootElement]), default);
        if (!valid)
        {
            await action.Should().ThrowAsync<NgbArgumentInvalidException>();
            source.Calls.Should().BeEmpty();
        }
        else
        {
            await action();
            source.Calls.Should().ContainSingle();
            source.Calls[0].Query.Predicates.Should().ContainSingle();
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Nested_groups_preserve_children_and_render_separate_subtotals(bool pivot, bool subtotals, bool separate)
    {
        var source = new Source();
        var plan = Plan(pivot) with
        {
            RowGroups = [new("group", "group", "Group", "string", false, GroupKey: "g"), new("child", "child", "Child", "string", false)],
            Shape = new(true, subtotals, separate, true, pivot),
            Sorts = [new("group", null, ReportSortDirection.Asc, GroupKey: "g"), new("group", null, ReportSortDirection.Asc), new("group", null, ReportSortDirection.Asc, GroupKey: "other"), new("unused", null, ReportSortDirection.Asc), new("amount", "amount", ReportSortDirection.Desc), new("column", null, ReportSortDirection.Asc, AppliesToColumnAxis: true)]
        };
        var result = await Executor(source).ExecuteAsync(Definition(), plan, new(), default);
        result.Sheet.Rows.Single(r => r.RowKind == ReportRowKind.Group).ChildrenPath!.Single().GetString().Should().Be("A");
        result.Sheet.Rows.Count(r => r.RowKind == ReportRowKind.Subtotal).Should().Be(separate ? 1 : 0);
        var groupRow = result.Sheet.Rows.Single(r => r.RowKind == ReportRowKind.Group);
        groupRow.Cells.Skip(1).Should().OnlyContain(c => c.Value == null);
        result.Sheet.Rows.Should().Contain(r => r.RowKind == ReportRowKind.Total);
        source.Calls.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, true)]
    public async Task Pivot_width_is_bounded_before_fetching_cells(int maxColumns, bool columnsOverflow)
    {
        var source = new Source { ColumnOverflow = columnsOverflow };
        var action = () => Executor(source).ExecuteAsync(Definition(maxColumns), Plan(true), new(), default);
        await action.Should().ThrowAsync<NgbArgumentInvalidException>().WithMessage("*columns*");
        source.Calls.Should().HaveCount(columnsOverflow ? 1 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pivot_rejects_cells_or_totals_outside_selected_keys(bool totals)
    {
        var source = new Source { CellOverflow = !totals, TotalsOverflow = totals };
        var action = () => Executor(source).ExecuteAsync(Definition(), Plan(true) with { Shape = new(true, true, false, true, true) }, new(), default);
        await action.Should().ThrowAsync<NgbInvariantViolationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_pivot_axis_produces_no_rows_and_does_not_fetch_cells(bool totals)
    {
        var source = new Source { EmptyColumns = true, NoDiagnostics = true };
        var result = await Executor(source).ExecuteAsync(Definition(), Plan(true) with { Shape = new(true, true, false, totals, true) }, new(), default);
        source.Calls.Should().HaveCount(totals ? 3 : 2);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Empty_pivot_row_axis_does_not_emit_an_orphaned_grand_total()
    {
        var source = new Source { EmptyRows = true };
        var page = await Executor(source).ExecuteAsync(Definition(), Plan(true) with { Shape = new(true, true, false, true, true) }, new(), default);
        page.Sheet.Rows.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Scalar_pivot_uses_no_row_selection_and_detail_pages_keep_source_diagnostics()
    {
        var source = new Source();
        var scalar = await Executor(source).ExecuteAsync(Definition(), Plan(true) with { RowGroups = [], DetailFields = [], Shape = new(false, false, false, true, true) }, new(), default);
        source.Calls.Should().OnlyContain(c => c.Selection == null);
        scalar.Sheet.Rows.Should().NotBeEmpty();
        source.Calls.Clear();
        var pivotDetail = await Executor(source).ExecuteAsync(Definition(), Plan(true) with { RowGroups = [], Shape = new(true, false, false, true, true) }, new(), default);
        pivotDetail.Sheet.Rows.Should().Contain(r => r.RowKind == ReportRowKind.Detail);
        pivotDetail.Sheet.Rows.Should().Contain(r => r.RowKind == ReportRowKind.Total);
        source.Calls.Clear();
        var detail = await Executor(source).ExecuteAsync(Definition(), Plan() with { RowGroups = [], Shape = new(true, false, false, true, false) }, new(), default);
        detail.Sheet.Rows.Should().Contain(r => r.RowKind == ReportRowKind.Detail);
        detail.Diagnostics!["source"].Should().Be("stub");
    }

    private static ReportPagedQueryExecutor Executor(IReportPageDataSource source) => new(source, Mock.Of<IDocumentDisplayReader>());
    private static ReportDefinitionRuntimeModel Definition(int? maxColumns = null) => new(new("test", "Test", Mode: ReportExecutionMode.Composable,
        Dataset: new("test", Fields: new[] { "group", "child", "column", "detail" }.Select(c => new ReportFieldDto(c, c, "string", ReportFieldKind.Attribute)).ToArray(),
            Measures: [new("amount", "Amount", "decimal", [ReportAggregationKind.Sum])]),
        Capabilities: new(MaxVisibleColumns: maxColumns)));
    private static ReportQueryPlan Plan(bool pivot = false) => new("test", "test", ReportExecutionMode.Composable,
        [new("group", "group", "Group", "string", false)], pivot ? [new("column", "column", "Column", "string", true)] : [],
        [new("amount", "amount", "Amount", "decimal", ReportAggregationKind.Sum)],
        [new("detail", "detail", "Detail", "string")], [], [], [], new(true, true, false, false, pivot), new(0, 10, null));

    private sealed class Source : IReportPageDataSource
    {
        public bool ColumnOverflow { get; init; }
        public bool CellOverflow { get; init; }
        public bool TotalsOverflow { get; init; }
        public bool EmptyColumns { get; init; }
        public bool EmptyRows { get; init; }
        public bool NoDiagnostics { get; init; }
        public List<(ReportDataQuery Query, ReportRowSelection? Selection)> Calls { get; } = [];
        public Task<ReportDataPage> ReadPageAsync(ReportDataQuery query, NGB.Application.Abstractions.Services.ReportPlanPaging paging, ReportRowSelection? selection, CancellationToken ct)
        {
            Calls.Add((query, selection));
            var column = query.ColumnGroups.Count > 0 && query.RowGroups.Count == 0 && query.DetailFields.Count == 0;
            var overflow = selection is not null ? CellOverflow : column && Calls.Count > 1 ? TotalsOverflow : column && ColumnOverflow;
            ReportDataRow[] rows = column && EmptyColumns || EmptyRows && (query.RowGroups.Count > 0 || query.DetailFields.Count > 0)
                ? [] : [new(new Dictionary<string, object?> { ["group"] = "A", ["child"] = "B", ["column"] = "C", ["detail"] = "D", ["amount"] = 12m })];
            return Task.FromResult(new ReportDataPage([], rows, 0, paging.Limit, null, overflow, Diagnostics: NoDiagnostics ? null : new Dictionary<string, string> { ["source"] = "stub" }));
        }
    }
}
