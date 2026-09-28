using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Api.IntegrationTests.Support;
using NGB.Trade.Runtime;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Reporting;

[CollectionDefinition("Trade report page boundaries", DisableParallelization = true)]
public sealed class TradeReportPageBoundariesCollection : ICollectionFixture<TradeReportPageBoundariesFixture>;

public sealed class TradeReportPageBoundariesFixture : IAsyncLifetime
{
    private readonly TradePostgresFixture _database = new();
    public IHost Host { get; private set; } = null!;
    public List<(Guid Item, Guid Party, string ItemDisplay, string PartyDisplay)> Records { get; } = [];

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        Host = TradeHostFactory.Create(_database.ConnectionString);
        await using var scope = Host.Services.CreateAsyncScope();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        await scope.ServiceProvider.GetRequiredService<ITradeSetupService>().EnsureDefaultsAsync(default);
        async Task<Guid> Lookup(string type, string display) =>
            (await catalogs.GetPageAsync(type, new PageRequestDto(0, 25, display), default)).Items.Single(x => x.Display == display).Id;
        async Task Post(string type, RecordPayload payload)
        {
            var draft = await documents.CreateDraftAsync(type, payload, default);
            await documents.PostAsync(type, draft.Id, default);
        }
        var unit = await Lookup(TradeCodes.UnitOfMeasure, "Each");
        var price = await Lookup(TradeCodes.PriceType, "Retail");
        var warehouse = await catalogs.CreateAsync(TradeCodes.Warehouse, TradePayloads.Payload(new
            { display = "Boundary warehouse", warehouse_code = "BOUNDARY", name = "Boundary warehouse", is_active = true }), default);

        // Immutable, posted application data. Date filters expose exact boundary cardinalities.
        // Equal amounts exercise seek pagination's secondary ordering across every page boundary.
        for (var i = 1; i <= 1000; i++)
        {
            var item = await catalogs.CreateAsync(TradeCodes.Item, TradePayloads.Payload(new
            {
                display = $"Boundary item {i:D4}", name = $"Boundary item {i:D4}", sku = $"B-{i:D4}",
                unit_of_measure_id = unit, default_sales_price_type_id = price, is_inventory_item = true, is_active = true
            }), default);
            var party = await catalogs.CreateAsync(TradeCodes.Party, TradePayloads.Payload(new
            {
                display = $"Boundary partner {i:D4}", name = $"Boundary partner {i:D4}",
                is_customer = true, is_vendor = true, is_active = true, default_currency = "USD"
            }), default);
            var day = i switch { <= 498 => 1, 499 => 2, 500 => 3, 501 => 4, <= 998 => 5, _ => 6 };
            var date = $"2026-04-{day:D2}";
            await Post(TradeCodes.PurchaseReceipt, TradePayloads.Payload(new
                { document_date_utc = date, vendor_id = party.Id, warehouse_id = warehouse.Id },
                TradePayloads.PurchaseReceiptLines(new TradePayloads.PurchaseReceiptLineRow(1, item.Id, 10, 5, 50))));
            await Post(TradeCodes.SalesInvoice, TradePayloads.Payload(new
                { document_date_utc = date, customer_id = party.Id, warehouse_id = warehouse.Id, price_type_id = price },
                TradePayloads.SalesInvoiceLines(new TradePayloads.SalesInvoiceLineRow(1, item.Id, 4, 10, 5, 40))));
            Records.Add((item.Id, party.Id, item.Display!, party.Display!));
        }
    }

    public async Task DisposeAsync()
    {
        Host?.Dispose();
        await _database.DisposeAsync();
    }
}

[Collection("Trade report page boundaries")]
public sealed class TradeReporting_PageBoundaries_P0Tests(TradeReportPageBoundariesFixture fixture)
{
    private static readonly string[] Codes =
        [TradeCodes.SalesByItemReport, TradeCodes.SalesByCustomerReport, TradeCodes.PurchasesByVendorReport];

    public static IEnumerable<object[]> Boundaries() =>
        from code in Codes from count in new[] { 0, 498, 499, 500, 501, 998, 1000 }
        from totals in new[] { true, false } select new object[] { code, count, totals };

    public static IEnumerable<object[]> ReportsAndTotals() =>
        from code in Codes from totals in new[] { true, false } select new object[] { code, totals };

    [Theory]
    [MemberData(nameof(Boundaries))]
    public Task Page_boundaries_preserve_every_group_and_global_totals(string code, int count, bool totals)
        => VerifyPages(code, Request(count, totals), Ids(code, count), count, totals);

    [Theory]
    [MemberData(nameof(ReportsAndTotals))]
    public async Task Page_sizes_and_changes_preserve_cursor_continuation(string code, bool totals)
    {
        foreach (var limit in new[] { 100, 499, 1000 })
            await VerifyPages(code, Request(1000, totals) with { Limit = limit }, Ids(code, 1000), 1000, totals);
        await VerifyPages(code, Request(1000, totals) with { Limit = 1 }, Ids(code, 1000), 1000, totals, changePageSize: true);
    }

    [Theory]
    [MemberData(nameof(ReportsAndTotals))]
    public Task Offset_pages_continue_without_skips_or_duplicate_groups(string code, bool totals)
        => VerifyPages(code, Request(1000, totals) with { Offset = 1 }, Ids(code, 1000).Skip(1).ToArray(), 1000, totals);

    [Theory]
    [MemberData(nameof(ReportsAndTotals))]
    public Task Filtered_totals_count_only_selected_groups(string code, bool totals)
    {
        var selected = Ids(code, 1000).Where((_, index) => index % 2 == 0).ToArray();
        var filter = code switch
        {
            TradeCodes.SalesByItemReport => "item_id",
            TradeCodes.SalesByCustomerReport => "customer_id",
            _ => "vendor_id"
        };
        return VerifyPages(code, Request(1000, totals) with
        {
            Filters = new Dictionary<string, ReportFilterValueDto>
                { [filter] = new(JsonSerializer.SerializeToElement(selected)) }
        }, selected, 500, totals);
    }

    [Theory]
    [InlineData(TradeCodes.SalesByItemReport)]
    [InlineData(TradeCodes.SalesByCustomerReport)]
    [InlineData(TradeCodes.PurchasesByVendorReport)]
    public async Task Default_layout_reserves_its_footer_and_unpaged_execution_does_not_silently_truncate(string code)
    {
        await VerifyPages(code, Request(1000, true) with { Layout = null }, Ids(code, 1000), 1000, true);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReportEngine>();
        var execute = () => engine.ExecuteAsync(code, Request(1000, true) with { DisablePaging = true }, default);
        await execute.Should().ThrowAsync<ReportLayoutValidationException>();
    }

    [Theory]
    [MemberData(nameof(ReportsAndTotals))]
    public async Task Xlsx_contains_all_groups_and_exactly_one_global_footer_when_enabled(string code, bool totals)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var input = Request(1000, totals);
        await using var download = await scope.ServiceProvider.GetRequiredService<IReportDownloadService>().PrepareAsync(
            code, new ReportExportRequestDto(Parameters: input.Parameters, Layout: input.Layout), default);
        using var content = new MemoryStream();
        await download.WriteAsync(content, default);
        content.Position = 0;
        using var zip = new ZipArchive(content, ZipArchiveMode.Read);
        using var worksheet = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(worksheet);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = xml.Descendants(ns + "row").ToArray();
        rows.Should().HaveCount(1001 + (totals ? 1 : 0), "one header, all 1000 groups, and the requested footer");
        var details = rows.Skip(1).Take(1000).ToArray();
        details.Select(row => row.Elements(ns + "c").First().Descendants(ns + "t").Single().Value)
            .Should().Equal(fixture.Records.Select(r => code == TradeCodes.SalesByItemReport ? r.ItemDisplay : r.PartyDisplay));
        var expectedRow = ExpectedNumbers(code, 1);
        foreach (var row in details)
            Numbers(row).Should().Equal(expectedRow);
        if (totals)
        {
            rows[^1].Elements(ns + "c").First().Descendants(ns + "t").Single().Value.Should().Be("Total");
            Numbers(rows[^1]).Should().Equal(ExpectedNumbers(code, 1000));
        }
        decimal[] Numbers(XElement row) => row.Elements(ns + "c").Skip(1)
            .Select(cell => decimal.Parse(cell.Element(ns + "v")!.Value, CultureInfo.InvariantCulture)).ToArray();
    }

    private async Task VerifyPages(string code, ReportExecutionRequestDto request, Guid[] expectedIds,
        int totalGroups, bool totals, bool changePageSize = false)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReportEngine>();
        var seen = new List<Guid>();
        var totalRows = 0;
        var pageNumber = 0;
        string? cursor = null;
        ReportExecutionResponseDto page;
        do
        {
            var limit = changePageSize && pageNumber > 0 ? 500 : request.Limit;
            page = await engine.ExecuteAsync(code, request with { Cursor = cursor, Limit = limit }, default);
            page.Offset.Should().Be(request.Offset + seen.Count, "summary rows must not advance the data cursor");
            page.Limit.Should().Be(Math.Min(limit, totals ? 499 : 500));
            page.Sheet.Rows.Count.Should().BeLessThanOrEqualTo(500);
            var details = page.Sheet.Rows.Where(row => row.RowKind == ReportRowKind.Detail).ToArray();
            details.Length.Should().Be(Math.Min(page.Limit, expectedIds.Length - seen.Count));
            foreach (var row in details)
            {
                row.Cells[0].Action.Should().NotBeNull();
                row.Cells[0].Action!.CatalogId.Should().NotBeNull();
                seen.Add(row.Cells[0].Action!.CatalogId!.Value);
                row.Cells.Skip(1).Select(cell => cell.Value!.Value.GetDecimal()).Should().Equal(ExpectedNumbers(code, 1));
            }
            foreach (var total in page.Sheet.Rows.Where(row => row.RowKind == ReportRowKind.Total))
            {
                page.HasMore.Should().BeFalse("the footer belongs only on the final page");
                total.SemanticRole.Should().Be("grand_total");
                total.Cells.Skip(1).Select(cell => cell.Value!.Value.GetDecimal()).Should().Equal(ExpectedNumbers(code, totalGroups));
                totalRows++;
            }
            page.HasMore.Should().Be(seen.Count < expectedIds.Length);
            if (page.HasMore)
            {
                page.NextCursor.Should().NotBeNullOrWhiteSpace();
                page.NextCursor.Should().NotBe(cursor, "each continuation must advance");
                page.Total.Should().BeNull();
            }
            cursor = page.NextCursor;
            (++pageNumber).Should().BeLessThan(20, "pagination must terminate");
        } while (page.HasMore);
        cursor.Should().BeNull();
        page.Total.Should().Be(totalGroups);
        seen.Should().Equal(expectedIds, "all groups must occur exactly once, in stable order");
        totalRows.Should().Be(totals && totalGroups > 0 ? 1 : 0);
    }

    private Guid[] Ids(string code, int count) => fixture.Records.Take(count)
        .Select(r => code == TradeCodes.SalesByItemReport ? r.Item : r.Party).ToArray();

    private static ReportExecutionRequestDto Request(int count, bool totals)
    {
        var to = count switch
        {
            0 => "2026-03-31", 498 => "2026-04-01", 499 => "2026-04-02", 500 => "2026-04-03",
            501 => "2026-04-04", 998 => "2026-04-05", 1000 => "2026-04-06",
            _ => throw new ArgumentOutOfRangeException(nameof(count))
        };
        return new ReportExecutionRequestDto(Limit: 500, Layout: new ReportLayoutDto(ShowGrandTotals: totals),
            Parameters: new Dictionary<string, string> { ["from_utc"] = "2026-03-01", ["to_utc"] = to });
    }

    private static decimal[] ExpectedNumbers(string code, int count) => code switch
    {
        TradeCodes.SalesByItemReport => [4m * count, 40m * count, 0, 0, 40m * count, 20m * count, 20m * count, 50],
        TradeCodes.SalesByCustomerReport => [count, 0, 40m * count, 0, 40m * count, 20m * count, 20m * count, 50],
        _ => [count, 0, 50m * count, 0, 50m * count]
    };
}
