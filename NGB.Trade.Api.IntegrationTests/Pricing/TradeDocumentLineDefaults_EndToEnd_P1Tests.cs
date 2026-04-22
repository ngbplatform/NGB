using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Contracts;
using NGB.Trade.Runtime;
using NGB.Trade.Runtime.Pricing;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Pricing;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeDocumentLineDefaults_EndToEnd_P1Tests(TradePostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SalesInvoice_Defaults_Use_CurrentPrice_And_LatestWarehouseCost_AsOfDocumentDate()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var defaults = scope.ServiceProvider.GetRequiredService<TradeDocumentLineDefaultsService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.UnitOfMeasure, "Each");
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.PriceType, "Retail");
        var warehouseId = await CreateWarehouseAsync(catalogs, "Florida Fulfillment Center", "MIA", "100 Harbor Blvd, Miami, FL");
        var vendorId = await CreatePartyAsync(catalogs, "Northshore Supply", "V-100", isCustomer: false, isVendor: true);
        var itemId = await CreateItemAsync(catalogs, unitOfMeasureId, retailPriceTypeId, "Extension Cord 25 ft", "EC-25");

        var purchaseReceipt = await documents.CreateDraftAsync(
            TradeCodes.PurchaseReceipt,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-01-15",
                    vendor_id = vendorId,
                    warehouse_id = warehouseId,
                    notes = "Inbound replenishment"
                },
                TradePayloads.PurchaseReceiptLines(
                    new TradePayloads.PurchaseReceiptLineRow(
                        Ordinal: 1,
                        ItemId: itemId,
                        Quantity: 40m,
                        UnitCost: 8.82m,
                        LineAmount: 352.80m))),
            CancellationToken.None);
        await documents.PostAsync(TradeCodes.PurchaseReceipt, purchaseReceipt.Id, CancellationToken.None);

        var itemPriceUpdate = await documents.CreateDraftAsync(
            TradeCodes.ItemPriceUpdate,
            TradePayloads.Payload(
                new
                {
                    effective_date = "2026-02-01",
                    notes = "Retail pricing refresh"
                },
                TradePayloads.ItemPriceUpdateLines(
                    new TradePayloads.ItemPriceUpdateLineRow(
                        Ordinal: 1,
                        ItemId: itemId,
                        PriceTypeId: retailPriceTypeId,
                        Currency: "usd",
                        UnitPrice: 17.73m))),
            CancellationToken.None);
        await documents.PostAsync(TradeCodes.ItemPriceUpdate, itemPriceUpdate.Id, CancellationToken.None);

        var response = await defaults.ResolveAsync(
            new TradeDocumentLineDefaultsRequestDto(
                DocumentType: TradeCodes.SalesInvoice,
                AsOfDate: "2026-04-02",
                WarehouseId: warehouseId,
                PriceTypeId: retailPriceTypeId,
                SalesInvoiceId: null,
                PurchaseReceiptId: null,
                Rows:
                [
                    new TradeDocumentLineDefaultsRowRequestDto("line-1", itemId, null)
                ]),
            CancellationToken.None);

        var row = response.Rows.Should().ContainSingle().Subject;
        row.RowKey.Should().Be("line-1");
        row.UnitPrice.Should().Be(17.73m);
        row.UnitCost.Should().Be(8.82m);
        row.Currency.Should().Be("USD");
        row.PriceType.Should().BeNull();
    }

    [Fact]
    public async Task CustomerReturn_Defaults_Copy_Price_And_Cost_From_SourceSalesInvoice()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var defaults = scope.ServiceProvider.GetRequiredService<TradeDocumentLineDefaultsService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.UnitOfMeasure, "Each");
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.PriceType, "Retail");
        var warehouseId = await CreateWarehouseAsync(catalogs, "Desert West Logistics Hub", "PHX", "4100 W Buckeye Rd, Phoenix, AZ");
        var vendorId = await CreatePartyAsync(catalogs, "Cardinal Safety Products", "V-180", isCustomer: false, isVendor: true);
        var customerId = await CreatePartyAsync(catalogs, "Bayview Stores", "C-300", isCustomer: true, isVendor: false);
        var itemId = await CreateItemAsync(catalogs, unitOfMeasureId, retailPriceTypeId, "Cable Management Kit", "CM-100");

        var purchaseReceipt = await documents.CreateDraftAsync(
            TradeCodes.PurchaseReceipt,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-03-28",
                    vendor_id = vendorId,
                    warehouse_id = warehouseId,
                    notes = "Inbound stock"
                },
                TradePayloads.PurchaseReceiptLines(
                    new TradePayloads.PurchaseReceiptLineRow(
                        Ordinal: 1,
                        ItemId: itemId,
                        Quantity: 30m,
                        UnitCost: 15.71m,
                        LineAmount: 471.30m))),
            CancellationToken.None);
        await documents.PostAsync(TradeCodes.PurchaseReceipt, purchaseReceipt.Id, CancellationToken.None);

        var salesInvoice = await documents.CreateDraftAsync(
            TradeCodes.SalesInvoice,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-01",
                    customer_id = customerId,
                    warehouse_id = warehouseId,
                    price_type_id = retailPriceTypeId,
                    notes = "Regional replenishment"
                },
                TradePayloads.SalesInvoiceLines(
                    new TradePayloads.SalesInvoiceLineRow(
                        Ordinal: 1,
                        ItemId: itemId,
                        Quantity: 12m,
                        UnitPrice: 29.56m,
                        UnitCost: 15.71m,
                        LineAmount: 354.72m))),
            CancellationToken.None);
        var postedSalesInvoice = await documents.PostAsync(TradeCodes.SalesInvoice, salesInvoice.Id, CancellationToken.None);

        var response = await defaults.ResolveAsync(
            new TradeDocumentLineDefaultsRequestDto(
                DocumentType: TradeCodes.CustomerReturn,
                AsOfDate: "2026-04-02",
                WarehouseId: warehouseId,
                PriceTypeId: null,
                SalesInvoiceId: postedSalesInvoice.Id,
                PurchaseReceiptId: null,
                Rows:
                [
                    new TradeDocumentLineDefaultsRowRequestDto("line-1", itemId, null)
                ]),
            CancellationToken.None);

        var row = response.Rows.Should().ContainSingle().Subject;
        row.UnitPrice.Should().Be(29.56m);
        row.UnitCost.Should().Be(15.71m);
        row.Currency.Should().BeNull();
    }

    [Fact]
    public async Task VendorReturn_Defaults_Copy_UnitCost_From_SourcePurchaseReceipt()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var defaults = scope.ServiceProvider.GetRequiredService<TradeDocumentLineDefaultsService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.UnitOfMeasure, "Each");
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.PriceType, "Retail");
        var warehouseId = await CreateWarehouseAsync(catalogs, "South Central Distribution Center", "DAL", "8200 Sterling St, Irving, TX");
        var vendorId = await CreatePartyAsync(catalogs, "Blue Horizon Distribution", "V-220", isCustomer: false, isVendor: true);
        var itemId = await CreateItemAsync(catalogs, unitOfMeasureId, retailPriceTypeId, "Storage Tote 27 Gallon", "ST-27");

        var purchaseReceipt = await documents.CreateDraftAsync(
            TradeCodes.PurchaseReceipt,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-03-15",
                    vendor_id = vendorId,
                    warehouse_id = warehouseId,
                    notes = "Inbound stock"
                },
                TradePayloads.PurchaseReceiptLines(
                    new TradePayloads.PurchaseReceiptLineRow(
                        Ordinal: 1,
                        ItemId: itemId,
                        Quantity: 25m,
                        UnitCost: 19.44m,
                        LineAmount: 486.00m))),
            CancellationToken.None);
        var postedPurchaseReceipt = await documents.PostAsync(TradeCodes.PurchaseReceipt, purchaseReceipt.Id, CancellationToken.None);

        var response = await defaults.ResolveAsync(
            new TradeDocumentLineDefaultsRequestDto(
                DocumentType: TradeCodes.VendorReturn,
                AsOfDate: "2026-04-02",
                WarehouseId: warehouseId,
                PriceTypeId: null,
                SalesInvoiceId: null,
                PurchaseReceiptId: postedPurchaseReceipt.Id,
                Rows:
                [
                    new TradeDocumentLineDefaultsRowRequestDto("line-1", itemId, null)
                ]),
            CancellationToken.None);

        var row = response.Rows.Should().ContainSingle().Subject;
        row.UnitCost.Should().Be(19.44m);
        row.UnitPrice.Should().BeNull();
    }

    [Fact]
    public async Task ItemPriceUpdate_Defaults_RowPriceType_And_CurrentUnitPrice_From_ItemProfile()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var defaults = scope.ServiceProvider.GetRequiredService<TradeDocumentLineDefaultsService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.UnitOfMeasure, "Each");
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.PriceType, "Retail");
        var itemId = await CreateItemAsync(catalogs, unitOfMeasureId, retailPriceTypeId, "Workbench 60 in", "WB-600");

        var itemPriceUpdate = await documents.CreateDraftAsync(
            TradeCodes.ItemPriceUpdate,
            TradePayloads.Payload(
                new
                {
                    effective_date = "2026-02-10",
                    notes = "Retail alignment"
                },
                TradePayloads.ItemPriceUpdateLines(
                    new TradePayloads.ItemPriceUpdateLineRow(
                        Ordinal: 1,
                        ItemId: itemId,
                        PriceTypeId: retailPriceTypeId,
                        Currency: "usd",
                        UnitPrice: 125.50m))),
            CancellationToken.None);
        await documents.PostAsync(TradeCodes.ItemPriceUpdate, itemPriceUpdate.Id, CancellationToken.None);

        var response = await defaults.ResolveAsync(
            new TradeDocumentLineDefaultsRequestDto(
                DocumentType: TradeCodes.ItemPriceUpdate,
                AsOfDate: "2026-03-01",
                WarehouseId: null,
                PriceTypeId: null,
                SalesInvoiceId: null,
                PurchaseReceiptId: null,
                Rows:
                [
                    new TradeDocumentLineDefaultsRowRequestDto("line-1", itemId, null)
                ]),
            CancellationToken.None);

        var row = response.Rows.Should().ContainSingle().Subject;
        row.PriceType.Should().NotBeNull();
        row.PriceType!.Id.Should().Be(retailPriceTypeId);
        row.PriceType.Display.Should().Be("Retail");
        row.UnitPrice.Should().Be(125.50m);
        row.Currency.Should().Be("USD");
    }

    private static async Task<Guid> CreateWarehouseAsync(
        ICatalogService catalogs,
        string display,
        string code,
        string address)
    {
        var warehouse = await catalogs.CreateAsync(
            TradeCodes.Warehouse,
            TradePayloads.Payload(new
            {
                display,
                name = display,
                warehouse_code = code,
                address,
                is_active = true
            }),
            CancellationToken.None);

        return warehouse.Id;
    }

    private static async Task<Guid> CreatePartyAsync(
        ICatalogService catalogs,
        string display,
        string partyNumber,
        bool isCustomer,
        bool isVendor)
    {
        var party = await catalogs.CreateAsync(
            TradeCodes.Party,
            TradePayloads.Payload(new
            {
                display,
                name = display,
                party_number = partyNumber,
                is_customer = isCustomer,
                is_vendor = isVendor,
                is_active = true
            }),
            CancellationToken.None);

        return party.Id;
    }

    private static async Task<Guid> CreateItemAsync(
        ICatalogService catalogs,
        Guid unitOfMeasureId,
        Guid retailPriceTypeId,
        string display,
        string sku)
    {
        var item = await catalogs.CreateAsync(
            TradeCodes.Item,
            TradePayloads.Payload(new
            {
                display,
                name = display,
                sku,
                unit_of_measure_id = unitOfMeasureId,
                default_sales_price_type_id = retailPriceTypeId,
                item_type = "Inventory",
                is_inventory_item = true,
                is_active = true
            }),
            CancellationToken.None);

        return item.Id;
    }

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
