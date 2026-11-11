using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportPagedQueryExecutorTotalsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_decimal_sum_pivots_reuse_root_and_column_totals_without_changing_the_sheet(bool allNull)
    {
        decimal?[] amounts = allNull ? [null, null, null] : [12.25m, null, -2.25m];
        var optimizedSource = new Source(amounts);
        var baselineSource = new Source(amounts);
        var actual = await Execute(optimizedSource, Plan(), new());
        // A continuation retains the original SQL-total route; the stub returns the
        // same rows so this compares the renderer and exact numeric/null results.
        var expected = await Execute(baselineSource, Plan(), new(Cursor: "existing-cursor"));

        JsonSerializer.Serialize(actual.Sheet).Should().Be(JsonSerializer.Serialize(expected.Sheet));
        optimizedSource.Calls.Should().HaveCount(3);
        baselineSource.Calls.Should().HaveCount(5);
        actual.Sheet.Rows.Should().ContainSingle(row => row.RowKind == ReportRowKind.Total);
    }

    [Theory]
    [InlineData("cursor")]
    [InlineData("average")]
    [InlineData("distinct-count")]
    [InlineData("overlapping-rows")]
    [InlineData("overlapping-columns")]
    [InlineData("floating-point")]
    public async Task Ineligible_pivots_keep_database_totals(string reason)
    {
        var plan = Plan();
        var request = new ReportExecutionRequestDto();
        switch (reason)
        {
            case "cursor":
                request = request with { Cursor = "existing-cursor" };
                break;
            case "average":
                plan = plan with { Measures = [plan.Measures[0] with { Aggregation = ReportAggregationKind.Average }] };
                break;
            case "distinct-count":
                plan = plan with { Measures = [plan.Measures[0] with { Aggregation = ReportAggregationKind.CountDistinct }] };
                break;
            case "overlapping-rows":
                plan = plan with { RowGroups = [plan.RowGroups[0] with { IncludeDescendants = true }] };
                break;
            case "overlapping-columns":
                plan = plan with { ColumnGroups = [plan.ColumnGroups[0] with { IncludeDescendants = true }] };
                break;
            default:
                plan = plan with { Measures = [plan.Measures[0] with { DataType = "double" }] };
                break;
        }

        var source = new Source([1m, 2m, 3m]);
        await Execute(source, plan, request);

        source.Calls.Should().HaveCount(5);
    }

    [Fact]
    public async Task Incomplete_row_pages_do_not_emit_or_compute_a_grand_total()
    {
        var source = new Source([1m, 2m, 3m]) { MoreRows = true };

        var page = await Execute(source, Plan(), new());

        page.HasMore.Should().BeTrue();
        page.Sheet.Rows.Should().NotContain(row => row.RowKind == ReportRowKind.Total);
        source.Calls.Should().HaveCount(3);
    }

    [Fact]
    public async Task Cancelling_decimal_groups_fall_back_to_database_precision_when_intermediate_sum_overflows()
    {
        var source = new Source([decimal.MaxValue, 1m, -decimal.MaxValue], total: 1m);

        var result = await Execute(source, Plan(), new());

        source.Calls.Should().HaveCount(5);
        result.Sheet.Rows.Single(row => row.RowKind == ReportRowKind.Total)
            .Cells.Last().Value!.Value.GetDecimal().Should().Be(1m);
    }

    [Fact]
    public async Task Cancelling_decimal_groups_fall_back_when_intermediate_addition_would_round()
    {
        var source = new Source([decimal.MaxValue - 1m, 0.25m, -(decimal.MaxValue - 1m)], total: 0.25m);

        var result = await Execute(source, Plan(), new());

        source.Calls.Should().HaveCount(5);
        result.Sheet.Rows.Single(row => row.RowKind == ReportRowKind.Total)
            .Cells.Last().Value!.Value.GetDecimal().Should().Be(0.25m);
    }

    [Fact]
    public async Task Decimal_measures_with_other_runtime_numeric_types_keep_database_totals()
    {
        var source = new Source([1m, 2m, 3m]) { WholeNumberValues = true };

        await Execute(source, Plan(), new());

        source.Calls.Should().HaveCount(5);
    }

    [Fact]
    public async Task Empty_row_axes_keep_database_null_totals()
    {
        var source = new Source([]);

        await Execute(source, Plan(), new());

        source.Calls.Should().HaveCount(4);
        source.Calls.Should().ContainSingle(query => query.RowGroups.Count == 0 && query.ColumnGroups.Count == 0);
    }

    private static Task<ReportExecutionResult> Execute(Source source, ReportQueryPlan plan, ReportExecutionRequestDto request)
    {
        var definition = new ReportDefinitionRuntimeModel(new("test", "Test", Mode: ReportExecutionMode.Composable,
            Dataset: new("test",
                Fields: [new("group", "Group", "string", ReportFieldKind.Attribute),
                         new("month", "Month", "string", ReportFieldKind.Attribute)],
                Measures: [new("amount", "Amount", "decimal", [ReportAggregationKind.Sum])])));
        return new ReportPagedQueryExecutor(source, Mock.Of<IDocumentDisplayReader>())
            .ExecuteAsync(definition, plan, request, default);
    }

    private static ReportQueryPlan Plan() => new("test", "test", ReportExecutionMode.Composable,
        [new("group", "group", "Group", "string", false)],
        [new("month", "month", "Month", "string", true)],
        [new("amount", "amount", "Amount", "decimal", ReportAggregationKind.Sum)],
        [], [], [], [], new(false, true, false, true, true), new(0, 10, null));

    private sealed class Source(decimal?[] amounts, decimal? total = null) : IReportPageDataSource
    {
        public bool MoreRows { get; init; }
        public bool WholeNumberValues { get; init; }
        public List<ReportDataQuery> Calls { get; } = [];

        public Task<ReportDataPage> ReadPageAsync(ReportDataQuery query,
            NGB.Application.Abstractions.Services.ReportPlanPaging paging,
            ReportRowSelection? selection, CancellationToken ct)
        {
            Calls.Add(query);
            var values = amounts.Any(value => value.HasValue) ? total ?? amounts.Sum() : null;
            var detail = selection is not null || query.RowGroups.Count > 0;
            var rows = detail
                ? amounts.Select((value, index) => Row(((char)('A' + index)).ToString(), value)).ToArray()
                : [Row(null, values)];
            return Task.FromResult(new ReportDataPage([], rows, 0, paging.Limit, null,
                MoreRows && query.RowGroups.Count > 0 && selection is null));
        }

        private ReportDataRow Row(string? group, decimal? value) => new(
            new Dictionary<string, object?>
            {
                ["group"] = group,
                ["month"] = "September",
                ["amount"] = WholeNumberValues && value.HasValue ? (object)decimal.ToInt64(value.Value) : value
            });
    }
}
