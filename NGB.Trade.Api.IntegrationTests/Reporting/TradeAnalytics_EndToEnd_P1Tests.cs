using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Runtime;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Reporting;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeAnalytics_EndToEnd_P1Tests(TradePostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Dashboard_And_SummaryReports_Return_NetTradeAnalytics()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var reports = scope.ServiceProvider.GetRequiredService<IReportEngine>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.UnitOfMeasure, "Each");
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.PriceType, "Retail");

        var warehouse = await catalogs.CreateAsync(
            TradeCodes.Warehouse,
            TradePayloads.Payload(new
            {
                display = "Main Warehouse",
                warehouse_code = "MAIN",
                name = "Main Warehouse",
                address = "100 Harbor Blvd, Miami, FL",
                is_active = true
            }),
            CancellationToken.None);

        var vendor = await catalogs.CreateAsync(
            TradeCodes.Party,
            TradePayloads.Payload(new
            {
                display = "Acme Supply",
                name = "Acme Supply",
                is_customer = false,
                is_vendor = true,
                is_active = true,
                default_currency = "USD"
            }),
            CancellationToken.None);

        var customer = await catalogs.CreateAsync(
            TradeCodes.Party,
            TradePayloads.Payload(new
            {
                display = "Northwind Retail",
                name = "Northwind Retail",
                is_customer = true,
                is_vendor = false,
                is_active = true,
                default_currency = "USD"
            }),
            CancellationToken.None);

        var alphaItem = await catalogs.CreateAsync(
            TradeCodes.Item,
            TradePayloads.Payload(new
            {
                display = "Alpha Widget",
                name = "Alpha Widget",
                sku = "AW-100",
                unit_of_measure_id = unitOfMeasureId,
                default_sales_price_type_id = retailPriceTypeId,
                is_inventory_item = true,
                is_active = true
            }),
            CancellationToken.None);

        var bravoItem = await catalogs.CreateAsync(
            TradeCodes.Item,
            TradePayloads.Payload(new
            {
                display = "Bravo Gadget",
                name = "Bravo Gadget",
                sku = "BG-200",
                unit_of_measure_id = unitOfMeasureId,
                default_sales_price_type_id = retailPriceTypeId,
                is_inventory_item = true,
                is_active = true
            }),
            CancellationToken.None);

        var purchaseReceipt = await documents.CreateDraftAsync(
            TradeCodes.PurchaseReceipt,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-05",
                    vendor_id = vendor.Id,
                    warehouse_id = warehouse.Id,
                    notes = "Initial stocking"
                },
                TradePayloads.PurchaseReceiptLines(
                    new TradePayloads.PurchaseReceiptLineRow(
                        Ordinal: 1,
                        ItemId: alphaItem.Id,
                        Quantity: 10m,
                        UnitCost: 5m,
                        LineAmount: 50m),
                    new TradePayloads.PurchaseReceiptLineRow(
                        Ordinal: 2,
                        ItemId: bravoItem.Id,
                        Quantity: 20m,
                        UnitCost: 3m,
                        LineAmount: 60m))),
            CancellationToken.None);

        await documents.PostAsync(TradeCodes.PurchaseReceipt, purchaseReceipt.Id, CancellationToken.None);

        var vendorReturn = await documents.CreateDraftAsync(
            TradeCodes.VendorReturn,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-08",
                    vendor_id = vendor.Id,
                    warehouse_id = warehouse.Id,
                    purchase_receipt_id = purchaseReceipt.Id,
                    notes = "Damaged stock"
                },
                TradePayloads.VendorReturnLines(
                    new TradePayloads.VendorReturnLineRow(
                        Ordinal: 1,
                        ItemId: bravoItem.Id,
                        Quantity: 2m,
                        UnitCost: 3m,
                        LineAmount: 6m))),
            CancellationToken.None);

        await documents.PostAsync(TradeCodes.VendorReturn, vendorReturn.Id, CancellationToken.None);

        var salesInvoice = await documents.CreateDraftAsync(
            TradeCodes.SalesInvoice,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-10",
                    customer_id = customer.Id,
                    warehouse_id = warehouse.Id,
                    price_type_id = retailPriceTypeId,
                    notes = "Customer shipment"
                },
                TradePayloads.SalesInvoiceLines(
                    new TradePayloads.SalesInvoiceLineRow(
                        Ordinal: 1,
                        ItemId: alphaItem.Id,
                        Quantity: 4m,
                        UnitPrice: 10m,
                        UnitCost: 5m,
                        LineAmount: 40m),
                    new TradePayloads.SalesInvoiceLineRow(
                        Ordinal: 2,
                        ItemId: bravoItem.Id,
                        Quantity: 5m,
                        UnitPrice: 8m,
                        UnitCost: 3m,
                        LineAmount: 40m))),
            CancellationToken.None);

        await documents.PostAsync(TradeCodes.SalesInvoice, salesInvoice.Id, CancellationToken.None);

        var customerReturn = await documents.CreateDraftAsync(
            TradeCodes.CustomerReturn,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-12",
                    customer_id = customer.Id,
                    warehouse_id = warehouse.Id,
                    sales_invoice_id = salesInvoice.Id,
                    notes = "Partial return"
                },
                TradePayloads.CustomerReturnLines(
                    new TradePayloads.CustomerReturnLineRow(
                        Ordinal: 1,
                        ItemId: alphaItem.Id,
                        Quantity: 1m,
                        UnitPrice: 10m,
                        UnitCost: 5m,
                        LineAmount: 10m))),
            CancellationToken.None);

        await documents.PostAsync(TradeCodes.CustomerReturn, customerReturn.Id, CancellationToken.None);

        var salesByItem = await reports.ExecuteAsync(
            TradeCodes.SalesByItemReport,
            new ReportExecutionRequestDto(
                DisablePaging: true,
                Parameters: BuildPeriod("2026-04-01", "2026-04-30")),
            CancellationToken.None);

        salesByItem.Total.Should().Be(2);
        salesByItem.Sheet.Rows.Should().HaveCount(3);
        salesByItem.Sheet.Rows.Should().Contain(row =>
            row.Cells[0].Display == "Bravo Gadget"
            && row.Cells[1].Display == "5"
            && row.Cells[2].Display == "40"
            && row.Cells[3].Display == "0"
            && row.Cells[5].Display == "40"
            && row.Cells[6].Display == "15"
            && row.Cells[7].Display == "25");
        salesByItem.Sheet.Rows.Should().Contain(row =>
            row.Cells[0].Display == "Alpha Widget"
            && row.Cells[1].Display == "4"
            && row.Cells[2].Display == "40"
            && row.Cells[3].Display == "1"
            && row.Cells[4].Display == "10"
            && row.Cells[5].Display == "30"
            && row.Cells[6].Display == "15"
            && row.Cells[7].Display == "15");

        var filteredSalesByItem = await reports.ExecuteAsync(
            TradeCodes.SalesByItemReport,
            new ReportExecutionRequestDto(
                DisablePaging: true,
                Parameters: BuildPeriod("2026-04-01", "2026-04-30"),
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = new(JsonSerializer.SerializeToElement(new[] { alphaItem.Id }))
                }),
            CancellationToken.None);

        filteredSalesByItem.Total.Should().Be(1);
        filteredSalesByItem.Sheet.Rows.Should().ContainSingle(row => row.RowKind == ReportRowKind.Detail);
        filteredSalesByItem.Sheet.Rows.Single(row => row.RowKind == ReportRowKind.Detail).Cells[0].Display.Should().Be("Alpha Widget");

        var salesByCustomer = await reports.ExecuteAsync(
            TradeCodes.SalesByCustomerReport,
            new ReportExecutionRequestDto(
                DisablePaging: true,
                Parameters: BuildPeriod("2026-04-01", "2026-04-30")),
            CancellationToken.None);

        salesByCustomer.Total.Should().Be(1);
        salesByCustomer.Sheet.Rows.Should().HaveCount(2);
        salesByCustomer.Sheet.Rows.Should().Contain(row =>
            row.Cells[0].Display == "Northwind Retail"
            && row.Cells[1].Display == "1"
            && row.Cells[2].Display == "1"
            && row.Cells[3].Display == "80"
            && row.Cells[4].Display == "10"
            && row.Cells[5].Display == "70"
            && row.Cells[6].Display == "30"
            && row.Cells[7].Display == "40");

        var purchasesByVendor = await reports.ExecuteAsync(
            TradeCodes.PurchasesByVendorReport,
            new ReportExecutionRequestDto(
                DisablePaging: true,
                Parameters: BuildPeriod("2026-04-01", "2026-04-30")),
            CancellationToken.None);

        purchasesByVendor.Total.Should().Be(1);
        purchasesByVendor.Sheet.Rows.Should().HaveCount(2);
        purchasesByVendor.Sheet.Rows.Should().Contain(row =>
            row.Cells[0].Display == "Acme Supply"
            && row.Cells[1].Display == "1"
            && row.Cells[2].Display == "1"
            && row.Cells[3].Display == "110"
            && row.Cells[4].Display == "6"
            && row.Cells[5].Display == "104");

        var dashboard = await reports.ExecuteAsync(
            TradeCodes.DashboardOverviewReport,
            new ReportExecutionRequestDto(
                DisablePaging: true,
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-04-30"
                }),
            CancellationToken.None);

        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[1].Display == "Sales This Month"
            && row.Cells[2].Display == "70");
        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[1].Display == "Purchases This Month"
            && row.Cells[2].Display == "104");
        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[1].Display == "Inventory On Hand"
            && row.Cells[2].Display == "20");
        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[1].Display == "Gross Margin"
            && row.Cells[2].Display == "40");
        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[0].Display == "Top Item"
            && row.Cells[1].Display == "Bravo Gadget"
            && row.Cells[2].Display == "40"
            && row.Cells[3].Display == "5");
        dashboard.Diagnostics.Should().NotBeNull();
        dashboard.Diagnostics!["inventory_position_count"].Should().Be("2");
        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[0].Display == "Inventory Position"
            && row.Cells[1].Display == "Bravo Gadget"
            && row.Cells[2].Display == "13"
            && row.Cells[3].Display == "Main Warehouse — 100 Harbor Blvd, Miami, FL");
        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[0].Display == "Recent Document"
            && row.Cells[4].Display!.Contains("Customer Return", StringComparison.Ordinal));
        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[0].Display == "Recent Document"
            && row.Cells[4].Display!.Contains("Sales Invoice", StringComparison.Ordinal));

        var groupedBalancesFirstPage = await reports.ExecuteAsync(
            TradeCodes.InventoryBalancesReport,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-04-30"
                },
                Limit: 2),
            CancellationToken.None);

        groupedBalancesFirstPage.Total.Should().Be(3);
        groupedBalancesFirstPage.HasMore.Should().BeTrue();
        groupedBalancesFirstPage.NextCursor.Should().NotBeNullOrWhiteSpace();
        groupedBalancesFirstPage.Sheet.Rows.Should().HaveCount(2);
        groupedBalancesFirstPage.Sheet.Rows.Should().NotContain(row => row.RowKind == ReportRowKind.Total);

        var groupedBalancesSecondPage = await reports.ExecuteAsync(
            TradeCodes.InventoryBalancesReport,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-04-30"
                },
                Limit: 2,
                Cursor: groupedBalancesFirstPage.NextCursor),
            CancellationToken.None);

        groupedBalancesSecondPage.Total.Should().Be(3);
        groupedBalancesSecondPage.HasMore.Should().BeFalse();
        groupedBalancesSecondPage.NextCursor.Should().BeNull();
        groupedBalancesSecondPage.Sheet.Rows.Should().HaveCount(2);
        groupedBalancesSecondPage.Sheet.Rows[0].Cells[0].Display.Should().Be("Bravo Gadget");
        groupedBalancesSecondPage.Sheet.Rows[1].RowKind.Should().Be(ReportRowKind.Total);
        groupedBalancesSecondPage.Sheet.Rows[1].Cells[1].Display.Should().Be("20");
    }

    private static Dictionary<string, string> BuildPeriod(string fromUtc, string toUtc)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["from_utc"] = fromUtc,
            ["to_utc"] = toUtc
        };

    private static async Task<Guid> GetCatalogIdByDisplayAsync(
        ICatalogService catalogs,
        string catalogType,
        string display)
    {
        var page = await catalogs.GetPageAsync(
            catalogType,
            new PageRequestDto(Offset: 0, Limit: 50, Search: null),
            CancellationToken.None);

        return page.Items.Single(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase)).Id;
    }
}
