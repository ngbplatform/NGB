using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.Reporting;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class DirectReportPagingTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
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
    private sealed class CountingSource(IReportPageDataSource inner, Counter counter) : IReportPageDataSource
    {
        public Task<ReportDataPage> ReadPageAsync(ReportDataQuery query, ReportPlanPaging paging, ReportRowSelection? selection, CancellationToken ct)
        {
            counter.Count++;
            return inner.ReadPageAsync(query, paging, selection, ct);
        }
    }
}
