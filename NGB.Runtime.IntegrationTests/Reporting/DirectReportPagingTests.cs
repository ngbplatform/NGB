using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Contracts.Common;
using NGB.PostgreSql.Reporting;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class DirectReportPagingTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(ReportSortDirection.Asc)]
    [InlineData(ReportSortDirection.Desc)]
    public async Task Wide_pivot_keys_reduce_page_size_without_losing_rows_or_adding_per_row_queries(ReportSortDirection direction)
    {
        var source = new WideSource();
        var queries = new Counter();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IReportDefinitionSource>(source);
            services.AddSingleton<IPostgresReportDatasetSource>(source);
            services.AddScoped<IReportPageDataSource>(sp => new CountingSource(sp.GetRequiredService<PostgresReportPlanExecutor>(), queries));
        });
        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReportEngine>();
        var request = new ReportExecutionRequestDto(Layout: new(
            ColumnGroups: [new("kind")], DetailFields: WideSource.Fields,
            Sorts: [new("f0", Direction: direction)],
            Measures: [new("amount", ReportAggregationKind.Sum)],
            ShowDetails: true, ShowSubtotals: false, ShowGrandTotals: false), Limit: PagingLimits.MaxPageSize);
        var ids = new List<long>();
        var expectedPageSize = ReportRowSelectionLimits.GetMaxKeyCount(WideSource.Fields.Length);
        string? cursor = null;
        do
        {
            var before = queries.Count;
            var page = await engine.ExecuteAsync(WideSource.Code, request with { Cursor = cursor }, default);
            var rows = page.Sheet.Rows.Where(row => row.RowKind == ReportRowKind.Detail).ToArray();
            rows.Length.Should().BeLessThanOrEqualTo(expectedPageSize);
            if (page.HasMore) rows.Should().HaveCount(expectedPageSize);
            foreach (var row in rows)
            {
                var id = row.Cells[1].Value!.Value.GetInt64();
                ids.Add(id);
                row.Cells[^2].Value!.Value.GetDecimal().Should().Be(id + 1);
                row.Cells[^1].Value!.Value.GetDecimal().Should().Be(id + 2);
            }
            (queries.Count - before).Should().Be(3, "one column-key query, one row-key query, and one cell query serve the whole page");
            cursor = page.NextCursor;
        } while (cursor is not null);

        var expected = Enumerable.Range(1, WideSource.RowCount)
            .OrderBy(id => id % 5 == 0)
            .ThenBy(id => id % 5 == 0 || direction == ReportSortDirection.Asc ? id : -id);
        ids.Should().Equal(expected.Select(id => (long)id));
    }

    [Theory]
    [InlineData(ReportAggregationKind.Sum, 1, 28)]
    [InlineData(ReportAggregationKind.Average, 2, 3.5)]
    [InlineData(ReportAggregationKind.CountDistinct, 2, 4)]
    public async Task Complete_additive_groups_reuse_their_totals_but_nonadditive_totals_read_original_observations(
        ReportAggregationKind aggregation, int expectedQueries, decimal expectedTotal)
    {
        var source = new DirectComposableExportsTests.Source("(VALUES (1),(1),(3),(5)) AS x(x) CROSS JOIN generate_series(1,2) y");
        var queries = new Counter();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IReportDefinitionSource>(source);
            services.AddSingleton<IPostgresReportDatasetSource>(source);
            services.AddScoped<IReportPageDataSource>(sp => new CountingSource(sp.GetRequiredService<PostgresReportPlanExecutor>(), queries));
        });
        await using var scope = host.Services.CreateAsyncScope();
        var page = await scope.ServiceProvider.GetRequiredService<IReportEngine>().ExecuteAsync(DirectComposableExportsTests.Source.Code,
            new(Layout: new(RowGroups: [new("customer_display")], Measures: [new("amount", aggregation)],
                ShowSubtotals: true, ShowGrandTotals: true), Limit: 200), default);
        page.HasMore.Should().BeFalse();
        queries.Count.Should().Be(expectedQueries);
        page.Sheet.Rows.Single(r => r.RowKind == ReportRowKind.Total).Cells.Last().Value!.Value.GetDecimal().Should().Be(expectedTotal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Groups_and_details_are_paged_with_constant_query_count_and_exact_global_averages(bool pivot)
    {
        var source = new DirectComposableExportsTests.Source();
        var queries = new Counter();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IReportDefinitionSource>(source);
            services.AddSingleton<IPostgresReportDatasetSource>(source);
            services.AddScoped<IReportPageDataSource>(sp => new CountingSource(sp.GetRequiredService<PostgresReportPlanExecutor>(), queries));
        });
        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReportEngine>();
        var request = new ReportExecutionRequestDto(Layout: new(RowGroups: [new("bucket")],
            ColumnGroups: pivot ? [new("kind")] : [], DetailFields: ["id"],
            Measures: [new("amount", ReportAggregationKind.Average)],
            ShowDetails: true, ShowSubtotals: true, ShowGrandTotals: true), Limit: 2);
        var root = await engine.ExecuteAsync(DirectComposableExportsTests.Source.Code, request, default);
        root.HasMore.Should().BeTrue();
        root.Sheet.Rows.Should().HaveCount(2);
        root.Sheet.Rows.Should().OnlyContain(r => r.RowKind == ReportRowKind.Group && r.ChildrenPath != null);
        queries.Count.Should().Be(pivot ? 3 : 1, "queries depend on the requested shape, never the number of groups");
        var next = await engine.ExecuteAsync(DirectComposableExportsTests.Source.Code, request with { Cursor = root.NextCursor }, default);
        next.HasMore.Should().BeFalse();
        next.Sheet.Rows.Single(r => r.RowKind == ReportRowKind.Total).Cells.Last().Value!.Value.GetDecimal().Should().Be(6002m);

        var childrenRequest = request with { GroupPath = root.Sheet.Rows[0].ChildrenPath, Limit = 271 };
        var ids = new List<long>();
        string? cursor = null;
        do
        {
            var before = queries.Count;
            var page = await engine.ExecuteAsync(DirectComposableExportsTests.Source.Code, childrenRequest with { Cursor = cursor }, default);
            page.Sheet.Columns.Should().BeEquivalentTo(root.Sheet.Columns, options => options.WithStrictOrdering());
            JsonSerializer.Serialize(page.Sheet.HeaderRows).Should().Be(JsonSerializer.Serialize(root.Sheet.HeaderRows));
            page.Sheet.Rows.Should().NotContain(row => row.RowKind == ReportRowKind.Total);
            var idColumn = page.Sheet.Columns.ToList().FindIndex(c => c.Code == "id");
            ids.AddRange(page.Sheet.Rows.Where(r => r.RowKind == ReportRowKind.Detail).Select(r => r.Cells[idColumn].Value!.Value.GetInt64()));
            (queries.Count - before).Should().BeLessThanOrEqualTo(pivot ? 5 : 2);
            cursor = page.NextCursor;
        } while (cursor is not null);
        ids.Should().Equal(Enumerable.Range(1, 4000).Select(i => (long)i));

        Func<Task> changed = () => engine.ExecuteAsync(DirectComposableExportsTests.Source.Code,
            request with { Cursor = root.NextCursor, GroupPath = [JsonSerializer.SerializeToElement(0)] }, default);
        await changed.Should().ThrowAsync<Exception>().WithMessage("*cursor*");
    }

    [Fact]
    public async Task Nested_pivot_branches_keep_the_root_columns_when_their_column_values_differ()
    {
        var source = new DirectComposableExportsTests.Source("(VALUES (1,1),(2,2),(4001,3)) AS xy(x,y)");
        var queries = new Counter();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IReportDefinitionSource>(source);
            services.AddSingleton<IPostgresReportDatasetSource>(source);
            services.AddScoped<IReportPageDataSource>(sp => new CountingSource(sp.GetRequiredService<PostgresReportPlanExecutor>(), queries));
        });
        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReportEngine>();
        var request = new ReportExecutionRequestDto(Layout: new(
            RowGroups: [new("bucket"), new("customer_display")], ColumnGroups: [new("kind")], DetailFields: ["id"],
            Measures: [new("amount", ReportAggregationKind.Sum)], ShowDetails: true, ShowSubtotals: true,
            ShowSubtotalsOnSeparateRows: false, ShowGrandTotals: false));
        var root = await engine.ExecuteAsync(DirectComposableExportsTests.Source.Code, request, default);
        root.Sheet.Rows.Should().HaveCount(2);
        var before = queries.Count;
        var branch = await engine.ExecuteAsync(DirectComposableExportsTests.Source.Code,
            request with { GroupPath = root.Sheet.Rows[0].ChildrenPath }, default);
        (queries.Count - before).Should().Be(3);
        branch.Sheet.Rows.Should().ContainSingle();
        branch.Sheet.Rows[0].ChildrenPath.Should().HaveCount(2);
        before = queries.Count;
        var details = await engine.ExecuteAsync(DirectComposableExportsTests.Source.Code,
            request with { GroupPath = branch.Sheet.Rows[0].ChildrenPath }, default);
        (queries.Count - before).Should().Be(3);
        details.Sheet.Rows.Should().HaveCount(2);
        foreach (var sheet in new[] { branch.Sheet, details.Sheet })
        {
            sheet.Columns.Should().BeEquivalentTo(root.Sheet.Columns, options => options.WithStrictOrdering());
            JsonSerializer.Serialize(sheet.HeaderRows).Should().Be(JsonSerializer.Serialize(root.Sheet.HeaderRows));
            sheet.Rows.Should().OnlyContain(row => row.Cells.Count == root.Sheet.Columns.Count);
            sheet.Rows.Should().OnlyContain(row => row.Cells.Last().Value == null, "the last pivot column belongs to the other root group");
        }
    }

    [Fact]
    public async Task Invalid_group_value_type_is_rejected_before_querying_the_database()
    {
        var source = new DirectComposableExportsTests.Source();
        var queries = new Counter();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IReportDefinitionSource>(source);
            services.AddSingleton<IPostgresReportDatasetSource>(source);
            services.AddScoped<IReportPageDataSource>(sp => new CountingSource(sp.GetRequiredService<PostgresReportPlanExecutor>(), queries));
        });
        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReportEngine>();
        Func<Task> execute = () => engine.ExecuteAsync(DirectComposableExportsTests.Source.Code,
            new(Layout: new(RowGroups: [new("customer_display")], DetailFields: ["id"],
                Measures: [new("amount", ReportAggregationKind.Sum)], ShowDetails: true),
                GroupPath: [JsonSerializer.SerializeToElement(1)]), default);
        await execute.Should().ThrowAsync<NgbArgumentInvalidException>().WithMessage("*group value type*");
        queries.Count.Should().Be(0);
    }

    [Fact]
    public async Task Full_download_exceeds_interactive_page_limits_without_saved_results()
    {
        var source = new DirectComposableExportsTests.Source();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IReportDefinitionSource>(source);
            services.AddSingleton<IPostgresReportDatasetSource>(source);
        });
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportDownloadService>();
        using var output = new MemoryStream();
        await using (var download = await service.PrepareAsync(DirectComposableExportsTests.Source.Code,
            new(Layout: new(DetailFields: ["id"], Measures: [new("amount", ReportAggregationKind.Average)], ShowGrandTotals: true)), default))
            await download.WriteAsync(output, default);
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        using var xml = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = XDocument.Load(xml).Descendants(ns + "row").ToArray();
        rows.Should().HaveCount(12003, "all 12001 details, one header and one grand total are exported");
        decimal.Parse(rows.Last().Elements(ns + "c").Last().Element(ns + "v")!.Value,
            System.Globalization.CultureInfo.InvariantCulture).Should().Be(6002m);
    }

    private sealed class Counter { public int Count; }

    private sealed class WideSource : IReportDefinitionSource, IPostgresReportDatasetSource
    {
        public const string Code = "it.wide_pivot";
        public const int RowCount = 47;
        public static readonly string[] Fields = Enumerable.Range(0, ReportLayoutLimits.MaxDetailFields).Select(i => $"f{i}").ToArray();

        public IReadOnlyList<ReportDefinitionDto> GetDefinitions() => [new(Code, "Wide pivot", Mode: ReportExecutionMode.Composable,
            Dataset: new(Code, Fields: Fields.Append("kind").Select(code => new ReportFieldDto(code, code, "int64", ReportFieldKind.Attribute,
                IsFilterable: true, IsGroupable: true, IsSortable: true, IsSelectable: true)).ToArray(),
                Measures: [new("amount", "Amount", "decimal", [ReportAggregationKind.Sum])]),
            Capabilities: new(AllowsRowGroups: true, AllowsColumnGroups: true, AllowsDetailFields: true, AllowsSorting: true,
                AllowsShowDetails: true, AllowsSubtotals: true, AllowsGrandTotals: true,
                MaxVisibleRows: PagingLimits.MaxPageSize, MaxRenderedCells: 50_000, MaxVisibleColumns: 512))];

        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() => [new(Code,
            $"generate_series(1,{RowCount}) x CROSS JOIN generate_series(1,2) y",
            Fields.Select((code, index) => new PostgresReportFieldBinding(code,
                    index == 0 || index == Fields.Length - 1 ? "CASE WHEN x % 5 = 0 THEN NULL ELSE x::bigint END" : "x::bigint", "int64"))
                .Append(new("kind", "y::bigint", "int64")).ToArray(),
            [new("amount", "(x+y)::numeric", "decimal")])];
    }

    private sealed class CountingSource(IReportPageDataSource inner, Counter counter) : IReportPageDataSource
    {
        public Task<ReportDataPage> ReadPageAsync(ReportDataQuery query, ReportPlanPaging paging, ReportRowSelection? selection, CancellationToken ct)
        {
            counter.Count++;
            return inner.ReadPageAsync(query, paging, selection, ct);
        }
    }
}
