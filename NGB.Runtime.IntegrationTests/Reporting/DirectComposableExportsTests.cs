using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Contracts.Metadata;
using NGB.PostgreSql.Reporting;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class DirectComposableExportsTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("flat")]
    [InlineData("grouped")]
    [InlineData("pivot")]
    public async Task Complete_large_reports_preserve_global_averages_pages_and_exports(string shape)
    {
        var data = new Source();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IReportDefinitionSource>(data);
            services.AddSingleton<IPostgresReportDatasetSource>(data);
        });
        await using var scope = host.Services.CreateAsyncScope();
        var request = new ReportExecutionRequestDto(Layout: new(
            RowGroups: shape == "flat" ? [] : [new("bucket")],
            ColumnGroups: shape == "pivot" ? [new("kind")] : [],
            DetailFields: ["id"], Measures: [new("amount", ReportAggregationKind.Average)],
            ShowDetails: true, ShowSubtotals: true, ShowGrandTotals: true));
        var sheet = await DirectAccountingExportsTests.RunAsync(host, Source.Code, request);
        sheet.Rows.Count(r => r.RowKind == ReportRowKind.Detail).Should().Be(12001);
        sheet.Rows.Single(r => r.RowKind == ReportRowKind.Total).Cells.Last().Value!.Value.GetDecimal().Should().Be(6002m);
        if (shape != "flat") sheet.Rows.Count(r => r.RowKind == ReportRowKind.Group).Should().Be(4);
    }

    [Theory]
    [InlineData(false, ReportAggregationKind.Sum, 28)]
    [InlineData(true, ReportAggregationKind.Sum, 28)]
    [InlineData(false, ReportAggregationKind.Average, 3.5)]
    [InlineData(true, ReportAggregationKind.Average, 3.5)]
    [InlineData(false, ReportAggregationKind.Min, 1)]
    [InlineData(true, ReportAggregationKind.Min, 1)]
    [InlineData(false, ReportAggregationKind.Max, 7)]
    [InlineData(true, ReportAggregationKind.Max, 7)]
    [InlineData(false, ReportAggregationKind.Count, 8)]
    [InlineData(true, ReportAggregationKind.Count, 8)]
    [InlineData(false, ReportAggregationKind.CountDistinct, 4)]
    [InlineData(true, ReportAggregationKind.CountDistinct, 4)]
    public async Task Subtotals_and_pivot_totals_use_original_observations_and_preserve_measure_sorting(bool pivot, ReportAggregationKind aggregation, decimal expected)
    {
        var source = new Source("(VALUES (1),(1),(3),(5)) AS x(x) CROSS JOIN generate_series(1,2) y");
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        { services.AddSingleton<IReportDefinitionSource>(source); services.AddSingleton<IPostgresReportDatasetSource>(source); });
        var sheet = await DirectAccountingExportsTests.RunAsync(host, Source.Code, new(Layout: new(
            RowGroups: [new("bucket")], ColumnGroups: pivot ? [new("kind")] : [], DetailFields: ["id"],
            Measures: [new("amount", aggregation)], Sorts: [new("amount", ReportSortDirection.Desc)],
            ShowDetails: true, ShowSubtotals: true, ShowGrandTotals: true)));
        sheet.Rows.Single(r => r.RowKind == ReportRowKind.Total).Cells.Last().Value!.Value.GetDecimal().Should().Be(expected);
        sheet.Rows.Single(r => r.RowKind == ReportRowKind.Group).Cells.Last().Value!.Value.GetDecimal().Should().Be(expected);
        if (aggregation == ReportAggregationKind.Average)
        {
            var id = sheet.Columns.ToList().FindIndex(c => c.Code == "id");
            sheet.Rows.Where(r => r.RowKind == ReportRowKind.Detail).Select(r => r.Cells[id].Value!.Value.GetInt64()).Should().Equal(5L, 3L, 1L);
        }
    }

    [Fact]
    public async Task A_measure_only_report_preserves_its_first_numeric_result()
    {
        var source = new Source();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        { services.AddSingleton<IReportDefinitionSource>(source); services.AddSingleton<IPostgresReportDatasetSource>(source); });
        var sheet = await DirectAccountingExportsTests.RunAsync(host, Source.Code, new(Layout: new(Measures: [new("amount", ReportAggregationKind.Average)], ShowGrandTotals: true)));
        sheet.Rows.Single().Cells.Single().Value!.Value.GetDecimal().Should().Be(6002m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_display_names_preserve_the_visible_grain_and_case_sensitive_pivot_values(bool pivot)
    {
        var source = new Source("(VALUES (1),(1),(3),(5)) AS x(x) CROSS JOIN generate_series(1,2) y");
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        { services.AddSingleton<IReportDefinitionSource>(source); services.AddSingleton<IPostgresReportDatasetSource>(source); });
        var sheet = await DirectAccountingExportsTests.RunAsync(host, Source.Code, new(Layout: new(
            RowGroups: [new("customer_display")], ColumnGroups: pivot ? [new("tag")] : [], DetailFields: ["id"],
            Measures: [new("amount", ReportAggregationKind.Average)], ShowDetails: true, ShowSubtotals: true, ShowGrandTotals: true)));
        var sameName = sheet.Rows.Single(r => r.RowKind == ReportRowKind.Group && r.Cells[0].Display == "Same name");
        sameName.Cells[0].Action.Should().BeNull("two distinct customers must not link to an arbitrary customer");
        sameName.Cells.Last().Value!.Value.GetDecimal().Should().BeApproximately(8m/3, 0.0000001m);
        var unique = sheet.Rows.Single(r => r.RowKind == ReportRowKind.Group && r.Cells[0].Display == "Other");
        unique.Cells[0].Action.Should().NotBeNull();
        if (pivot)
        {
            var total = sheet.Rows.Last();
            new[] { total.Cells[^3].Value!.Value.GetDecimal(), total.Cells[^2].Value!.Value.GetDecimal() }
                .Should().BeEquivalentTo(new[] { 2.5m, 4.5m });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_date_fields_preserve_independent_time_grains_and_axis_sorting(bool monthOnColumns)
    {
        var source = new Source("(VALUES (1),(5),(13)) AS x(x) CROSS JOIN generate_series(1,2) y");
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        { services.AddSingleton<IReportDefinitionSource>(source); services.AddSingleton<IPostgresReportDatasetSource>(source); });
        var year = new ReportGroupingDto("date", ReportTimeGrain.Year, GroupKey: "year");
        var month = new ReportGroupingDto("date", ReportTimeGrain.Month, GroupKey: "month");
        var sheet = await DirectAccountingExportsTests.RunAsync(host, Source.Code, new(Layout: new(
            RowGroups: monthOnColumns ? [year] : [year, month], ColumnGroups: monthOnColumns ? [month] : [new("kind")], DetailFields: ["id"],
            Sorts: [new("date", ReportSortDirection.Desc, ReportTimeGrain.Year, GroupKey: "year"),
                new("date", ReportSortDirection.Asc, ReportTimeGrain.Month, AppliesToColumnAxis: monthOnColumns, GroupKey: "month")],
            Measures: [new("amount", ReportAggregationKind.Average)], ShowDetails: true, ShowSubtotals: true,
            ShowSubtotalsOnSeparateRows: true, ShowGrandTotals: true)));
        var id = sheet.Columns.ToList().FindIndex(c => c.Code == "id");
        sheet.Rows.Where(r => r.RowKind == ReportRowKind.Detail).Select(r => r.Cells[id].Value!.Value.GetInt64()).Should().Equal(13L, 1L, 5L);
        sheet.Rows.Last().Cells.Last().Value!.Value.GetDecimal().Should().BeApproximately(22m/3, 0.0000001m);
        sheet.Rows.Count(r => r.RowKind == ReportRowKind.Group).Should().Be(monthOnColumns ? 2 : 5);
        sheet.Rows.Count(r => r.RowKind == ReportRowKind.Subtotal).Should().Be(monthOnColumns ? 0 : 2);
    }

    [Fact]
    public async Task Source_pages_and_separate_total_queries_share_a_repeatable_read_snapshot()
    {
        var source = new Source("it_saved_rows x CROSS JOIN generate_series(1,2) y");
        var gate = new ReadGate();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IReportDefinitionSource>(source);
            services.AddSingleton<IPostgresReportDatasetSource>(source);
            services.AddScoped<IStreamingReportDataSource>(sp => new PausingSource(sp.GetRequiredService<PostgresReportPlanExecutor>(), gate));
        });
        await DirectAccountingExportsTests.ExecuteAsync(host,
            "CREATE TABLE it_saved_rows(x integer); INSERT INTO it_saved_rows SELECT generate_series(1,1001);", null);
        await using var scope = host.Services.CreateAsyncScope();
        var processing = DirectAccountingExportsTests.RunAsync(host, Source.Code, new(Layout: new(DetailFields: ["id"],
            Measures: [new("amount", ReportAggregationKind.Average)], ShowGrandTotals: true)));
        try
        {
            await gate.Read.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await DirectAccountingExportsTests.ExecuteAsync(host, "INSERT INTO it_saved_rows VALUES (1000000);", null);
        }
        finally { gate.Resume.TrySetResult(); }
        var sheet = await processing;
        sheet.Rows.Should().HaveCount(1002);
        sheet.Rows.Last().Cells.Last().Value!.Value.GetDecimal().Should().Be(502m);
        await DirectAccountingExportsTests.ExecuteAsync(host, "DROP TABLE it_saved_rows;", null);
    }

    private sealed class ReadGate
    {
        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Paused;
    }
    private sealed class PausingSource(PostgresReportPlanExecutor inner, ReadGate gate) : IStreamingReportDataSource
    {
        public async IAsyncEnumerable<ReportDataPage> ReadAsync(ReportDataQuery query, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var page in inner.ReadAsync(query, ct))
            {
                if (!gate.Paused)
                {
                    gate.Paused = true;
                    gate.Read.TrySetResult();
                    await gate.Resume.Task.WaitAsync(ct);
                }
                yield return page;
            }
        }
    }

    internal sealed class Source(string fromSql = "generate_series(1,12001) x CROSS JOIN generate_series(1,2) y") : IReportDefinitionSource, IPostgresReportDatasetSource
    {
        public const string Code = "it.large_report";
        public IReadOnlyList<ReportDefinitionDto> GetDefinitions() => [new(Code, "Large report", Mode: ReportExecutionMode.Composable,
            Dataset: new(Code, Fields: [Field("id"), Field("bucket"), Field("kind"),
                Field("customer_display") with { DataType = "string" },
                Field("customer_id") with { DataType = "uuid", Lookup = new CatalogLookupSourceDto("it.customer") },
                Field("tag") with { DataType = "string" },
                Field("date") with { DataType = "date", Kind = ReportFieldKind.Time, SupportedTimeGrains = [ReportTimeGrain.Year, ReportTimeGrain.Month] }],
                Measures: [new("amount", "Amount", "decimal", [ReportAggregationKind.Sum, ReportAggregationKind.Average, ReportAggregationKind.Min, ReportAggregationKind.Max, ReportAggregationKind.Count, ReportAggregationKind.CountDistinct])]),
            Capabilities: new(AllowsRowGroups: true, AllowsColumnGroups: true, AllowsDetailFields: true, AllowsSorting: true,
                AllowsShowDetails: true, AllowsSubtotals: true, AllowsSeparateRowSubtotals: true, AllowsGrandTotals: true,
                AllowsXlsxExport: true, MaxVisibleRows: 500, MaxRenderedCells: 2000, MaxVisibleColumns: 80))];
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() => [new(Code,
            fromSql,
            [new("id", "x::bigint", "int64"), new("bucket", "((x-1)/4000)::bigint", "int64"), new("kind", "y::bigint", "int64"),
                new("customer_display", "CASE WHEN x<5 THEN 'Same name' ELSE 'Other' END", "string"),
                new("customer_id", "md5(x::text)::uuid", "uuid"), new("tag", "CASE y WHEN 1 THEN 'A|tag=null' ELSE 'a|tag=null' END", "string"),
                new("date", "make_date(2025+x/12,x%12+1,1)", "date", monthBucketSqlExpression: "make_date(2025+x/12,x%12+1,1)", yearBucketSqlExpression: "make_date(2025+x/12,1,1)")],
            [new("amount", "(x+(y-1)*2)::numeric", "decimal")])];
        private static ReportFieldDto Field(string code) => new(code, code, "int64", ReportFieldKind.Attribute,
            IsFilterable: true, IsGroupable: true, IsSortable: true, IsSelectable: true);
    }
}
