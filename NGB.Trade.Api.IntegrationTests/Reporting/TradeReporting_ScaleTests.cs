using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Testing.Reporting;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Api.IntegrationTests.Support;
using NGB.Trade.Runtime;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Reporting;

[CollectionDefinition("Trade report scale", DisableParallelization = true)]
public sealed class TradeReportScaleCollection : ICollectionFixture<TradeReportScaleFixture>;

public sealed class TradeReportScaleFixture : IAsyncLifetime
{
    private readonly TradePostgresFixture _database = new();
    public IHost Host { get; private set; } = null!;
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
        async Task<Guid> Catalog(string type, object value) =>
            (await catalogs.CreateAsync(type, TradePayloads.Payload(value), default)).Id;
        async Task Post(string type, RecordPayload payload)
        {
            var draft = await documents.CreateDraftAsync(type, payload, default);
            await documents.PostAsync(type, draft.Id, default);
        }
        var unit = await Lookup(TradeCodes.UnitOfMeasure, "Each");
        var price = await Lookup(TradeCodes.PriceType, "Retail");
        var warehouse = await Catalog(TradeCodes.Warehouse, new { display = "Scale", warehouse_code = "SCALE", name = "Scale", is_active = true });
        var items = new List<Guid>();
        var parties = new List<Guid>();
        for (var i = 0; i < 256; i++)
        {
            items.Add(await Catalog(TradeCodes.Item, new { display = $"Item {i:D4}", name = $"Item {i:D4}", sku = $"S-{i:D4}",
                unit_of_measure_id = unit, default_sales_price_type_id = price, is_inventory_item = true, is_active = true }));
            parties.Add(await Catalog(TradeCodes.Party, new { display = $"Partner {i:D4}", name = $"Partner {i:D4}",
                is_customer = true, is_vendor = true, is_active = true, default_currency = "USD" }));
        }
        // 25,600 posted inventory movements, 256 independent partners/items and 10,240 historical prices.
        // Build through application services so dimensions, movements and document status agree.
        for (var i = 0; i < parties.Count; i++)
        {
            var selected = Enumerable.Range(0, 50).Select(j => items[(i + j) % items.Count]).ToArray();
            await Post(TradeCodes.PurchaseReceipt, TradePayloads.Payload(new { document_date_utc = "2026-04-05", vendor_id = parties[i], warehouse_id = warehouse },
                TradePayloads.PurchaseReceiptLines(selected.Select((item, j) => new TradePayloads.PurchaseReceiptLineRow(j + 1, item, 10, 5, 50)).ToArray())));
            await Post(TradeCodes.SalesInvoice, TradePayloads.Payload(new { document_date_utc = "2026-04-10", customer_id = parties[i], warehouse_id = warehouse, price_type_id = price },
                TradePayloads.SalesInvoiceLines(selected.Select((item, j) => new TradePayloads.SalesInvoiceLineRow(j + 1, item, 4, 10, 5, 40)).ToArray())));
        }
        for (var day = 0; day < 40; day++)
            await Post(TradeCodes.ItemPriceUpdate, TradePayloads.Payload(new { effective_date = new DateOnly(2026, 3, 1).AddDays(day).ToString("yyyy-MM-dd") },
                TradePayloads.ItemPriceUpdateLines(items.Select((item, j) => new TradePayloads.ItemPriceUpdateLineRow(j + 1, item, price, "USD", 10 + day)).ToArray())));
    }
    public async Task DisposeAsync() { Host?.Dispose(); await _database.DisposeAsync(); }
}

[Collection("Trade report scale")]
public sealed class TradeReporting_ScaleTests(TradeReportScaleFixture fixture)
{
    [Theory]
    [InlineData(TradeCodes.SalesByItemReport)]
    [InlineData(TradeCodes.SalesByCustomerReport)]
    [InlineData(TradeCodes.PurchasesByVendorReport)]
    [InlineData(TradeCodes.InventoryBalancesReport)]
    [InlineData(TradeCodes.InventoryMovementsReport)]
    [InlineData(TradeCodes.CurrentItemPricesReport)]
    [InlineData(TradeCodes.DashboardOverviewReport)]
    public async Task Reports_bound_page_queries_and_complete_exports_on_a_large_posted_dataset(string code)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var parameters = code == TradeCodes.CurrentItemPricesReport ? null
            : code is TradeCodes.InventoryBalancesReport or TradeCodes.DashboardOverviewReport
                ? new Dictionary<string, string> { ["as_of_utc"] = "2026-04-30" }
                : new Dictionary<string, string> { ["from_utc"] = "2026-04-01", ["to_utc"] = "2026-04-30" };
        await ReportScaleAssertions.VerifyAsync(scope.ServiceProvider.GetRequiredService<IReportEngine>(),
            scope.ServiceProvider.GetRequiredService<IReportDownloadService>(), code, new ReportExecutionRequestDto(Parameters: parameters),
            minimumExportRows: code == TradeCodes.DashboardOverviewReport ? 42 : code == TradeCodes.InventoryMovementsReport ? 25600 : 256);
    }
}
