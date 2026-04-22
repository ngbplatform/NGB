using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Tools.Exceptions;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Runtime;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Validation;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeDocumentReferences_EndToEnd_P1Tests(TradePostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CustomerReturn_Rejects_SalesInvoice_From_Different_Customer()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var scenario = await SeedReferenceScenarioAsync(scope.ServiceProvider);
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var draft = await documents.CreateDraftAsync(
            TradeCodes.CustomerReturn,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-12",
                    customer_id = scenario.CustomerBId,
                    warehouse_id = scenario.WarehouseId,
                    sales_invoice_id = scenario.PostedSalesInvoiceId,
                    notes = "Wrong customer should fail"
                },
                TradePayloads.CustomerReturnLines(
                    new TradePayloads.CustomerReturnLineRow(1, scenario.ItemId, 1m, 20m, 12m, 20m))),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.CustomerReturn, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("sales_invoice_id");
        ex.Which.Reason.Should().Be("Referenced Sales Invoice must belong to the selected customer.");
    }

    [Fact]
    public async Task CustomerPayment_Rejects_SalesInvoice_From_Different_Customer()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var scenario = await SeedReferenceScenarioAsync(scope.ServiceProvider);
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var draft = await documents.CreateDraftAsync(
            TradeCodes.CustomerPayment,
            TradePayloads.Payload(new
            {
                document_date_utc = "2026-04-12",
                customer_id = scenario.CustomerBId,
                sales_invoice_id = scenario.PostedSalesInvoiceId,
                amount = 25m,
                notes = "Wrong customer should fail"
            }),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.CustomerPayment, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("sales_invoice_id");
        ex.Which.Reason.Should().Be("Referenced Sales Invoice must belong to the selected customer.");
    }

    [Fact]
    public async Task VendorReturn_Rejects_PurchaseReceipt_From_Different_Vendor()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var scenario = await SeedReferenceScenarioAsync(scope.ServiceProvider);
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var draft = await documents.CreateDraftAsync(
            TradeCodes.VendorReturn,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-12",
                    vendor_id = scenario.VendorBId,
                    warehouse_id = scenario.WarehouseId,
                    purchase_receipt_id = scenario.PostedPurchaseReceiptId,
                    notes = "Wrong vendor should fail"
                },
                TradePayloads.VendorReturnLines(
                    new TradePayloads.VendorReturnLineRow(1, scenario.ItemId, 1m, 12m, 12m))),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.VendorReturn, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("purchase_receipt_id");
        ex.Which.Reason.Should().Be("Referenced Purchase Receipt must belong to the selected vendor.");
    }

    [Fact]
    public async Task VendorPayment_Rejects_PurchaseReceipt_From_Different_Vendor()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var scenario = await SeedReferenceScenarioAsync(scope.ServiceProvider);
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var draft = await documents.CreateDraftAsync(
            TradeCodes.VendorPayment,
            TradePayloads.Payload(new
            {
                document_date_utc = "2026-04-12",
                vendor_id = scenario.VendorBId,
                purchase_receipt_id = scenario.PostedPurchaseReceiptId,
                amount = 25m,
                notes = "Wrong vendor should fail"
            }),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.VendorPayment, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("purchase_receipt_id");
        ex.Which.Reason.Should().Be("Referenced Purchase Receipt must belong to the selected vendor.");
    }

    [Fact]
    public async Task CustomerReturn_Rejects_Draft_SalesInvoice_Reference()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var scenario = await SeedReferenceScenarioAsync(scope.ServiceProvider);
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var draft = await documents.CreateDraftAsync(
            TradeCodes.CustomerReturn,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-12",
                    customer_id = scenario.CustomerAId,
                    warehouse_id = scenario.WarehouseId,
                    sales_invoice_id = scenario.DraftSalesInvoiceId,
                    notes = "Draft invoice should fail"
                },
                TradePayloads.CustomerReturnLines(
                    new TradePayloads.CustomerReturnLineRow(1, scenario.ItemId, 1m, 20m, 12m, 20m))),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.CustomerReturn, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("sales_invoice_id");
        ex.Which.Reason.Should().Be("Referenced Sales Invoice must be posted.");
    }

    [Fact]
    public async Task CustomerPayment_Rejects_Draft_SalesInvoice_Reference()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var scenario = await SeedReferenceScenarioAsync(scope.ServiceProvider);
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var draft = await documents.CreateDraftAsync(
            TradeCodes.CustomerPayment,
            TradePayloads.Payload(new
            {
                document_date_utc = "2026-04-12",
                customer_id = scenario.CustomerAId,
                sales_invoice_id = scenario.DraftSalesInvoiceId,
                amount = 25m,
                notes = "Draft invoice should fail"
            }),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.CustomerPayment, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("sales_invoice_id");
        ex.Which.Reason.Should().Be("Referenced Sales Invoice must be posted.");
    }

    [Fact]
    public async Task VendorReturn_Rejects_Draft_PurchaseReceipt_Reference()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var scenario = await SeedReferenceScenarioAsync(scope.ServiceProvider);
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var draft = await documents.CreateDraftAsync(
            TradeCodes.VendorReturn,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-12",
                    vendor_id = scenario.VendorAId,
                    warehouse_id = scenario.WarehouseId,
                    purchase_receipt_id = scenario.DraftPurchaseReceiptId,
                    notes = "Draft receipt should fail"
                },
                TradePayloads.VendorReturnLines(
                    new TradePayloads.VendorReturnLineRow(1, scenario.ItemId, 1m, 12m, 12m))),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.VendorReturn, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("purchase_receipt_id");
        ex.Which.Reason.Should().Be("Referenced Purchase Receipt must be posted.");
    }

    [Fact]
    public async Task VendorPayment_Rejects_Draft_PurchaseReceipt_Reference()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var scenario = await SeedReferenceScenarioAsync(scope.ServiceProvider);
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var draft = await documents.CreateDraftAsync(
            TradeCodes.VendorPayment,
            TradePayloads.Payload(new
            {
                document_date_utc = "2026-04-12",
                vendor_id = scenario.VendorAId,
                purchase_receipt_id = scenario.DraftPurchaseReceiptId,
                amount = 25m,
                notes = "Draft receipt should fail"
            }),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.VendorPayment, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("purchase_receipt_id");
        ex.Which.Reason.Should().Be("Referenced Purchase Receipt must be posted.");
    }

    private static async Task<ReferenceScenario> SeedReferenceScenarioAsync(IServiceProvider services)
    {
        var setup = services.GetRequiredService<ITradeSetupService>();
        var catalogs = services.GetRequiredService<ICatalogService>();
        var documents = services.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.UnitOfMeasure, "Each");
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.PriceType, "Retail");

        var warehouseId = await CreateWarehouseAsync(catalogs, "Reference Validation Warehouse", "REF", "100 Harbor Blvd, Miami, FL");
        var vendorAId = await CreatePartyAsync(catalogs, "Northstar Supply", "V-100", isCustomer: false, isVendor: true);
        var vendorBId = await CreatePartyAsync(catalogs, "Blue Ridge Supply", "V-200", isCustomer: false, isVendor: true);
        var customerAId = await CreatePartyAsync(catalogs, "Bayview Stores", "C-100", isCustomer: true, isVendor: false);
        var customerBId = await CreatePartyAsync(catalogs, "Harbor Retail", "C-200", isCustomer: true, isVendor: false);
        var itemId = await CreateItemAsync(catalogs, unitOfMeasureId, retailPriceTypeId, "Cable Management Kit", "CMK-100");

        var postedPurchaseReceipt = await documents.CreateDraftAsync(
            TradeCodes.PurchaseReceipt,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-05",
                    vendor_id = vendorAId,
                    warehouse_id = warehouseId,
                    notes = "Posted reference receipt"
                },
                TradePayloads.PurchaseReceiptLines(
                    new TradePayloads.PurchaseReceiptLineRow(1, itemId, 10m, 12m, 120m))),
            CancellationToken.None);

        await documents.PostAsync(TradeCodes.PurchaseReceipt, postedPurchaseReceipt.Id, CancellationToken.None);

        var postedSalesInvoice = await documents.CreateDraftAsync(
            TradeCodes.SalesInvoice,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-08",
                    customer_id = customerAId,
                    warehouse_id = warehouseId,
                    price_type_id = retailPriceTypeId,
                    notes = "Posted reference invoice"
                },
                TradePayloads.SalesInvoiceLines(
                    new TradePayloads.SalesInvoiceLineRow(1, itemId, 2m, 20m, 12m, 40m))),
            CancellationToken.None);

        await documents.PostAsync(TradeCodes.SalesInvoice, postedSalesInvoice.Id, CancellationToken.None);

        var draftPurchaseReceipt = await documents.CreateDraftAsync(
            TradeCodes.PurchaseReceipt,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-09",
                    vendor_id = vendorAId,
                    warehouse_id = warehouseId,
                    notes = "Draft reference receipt"
                },
                TradePayloads.PurchaseReceiptLines(
                    new TradePayloads.PurchaseReceiptLineRow(1, itemId, 1m, 12m, 12m))),
            CancellationToken.None);

        var draftSalesInvoice = await documents.CreateDraftAsync(
            TradeCodes.SalesInvoice,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-09",
                    customer_id = customerAId,
                    warehouse_id = warehouseId,
                    price_type_id = retailPriceTypeId,
                    notes = "Draft reference invoice"
                },
                TradePayloads.SalesInvoiceLines(
                    new TradePayloads.SalesInvoiceLineRow(1, itemId, 1m, 20m, 12m, 20m))),
            CancellationToken.None);

        return new ReferenceScenario(
            WarehouseId: warehouseId,
            VendorAId: vendorAId,
            VendorBId: vendorBId,
            CustomerAId: customerAId,
            CustomerBId: customerBId,
            ItemId: itemId,
            PostedPurchaseReceiptId: postedPurchaseReceipt.Id,
            DraftPurchaseReceiptId: draftPurchaseReceipt.Id,
            PostedSalesInvoiceId: postedSalesInvoice.Id,
            DraftSalesInvoiceId: draftSalesInvoice.Id);
    }

    private static async Task<Guid> CreateWarehouseAsync(
        ICatalogService catalogs,
        string display,
        string warehouseCode,
        string address)
    {
        var warehouse = await catalogs.CreateAsync(
            TradeCodes.Warehouse,
            TradePayloads.Payload(new
            {
                display,
                warehouse_code = warehouseCode,
                name = display,
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
                party_number = partyNumber,
                name = display,
                is_customer = isCustomer,
                is_vendor = isVendor,
                is_active = true,
                default_currency = "USD"
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
                item_type = "Inventory",
                is_inventory_item = true,
                default_sales_price_type_id = retailPriceTypeId,
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
        var page = await catalogs.GetPageAsync(catalogType, new PageRequestDto(0, 50, display), CancellationToken.None);
        return page.Items.Single(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase)).Id;
    }

    private sealed record ReferenceScenario(
        Guid WarehouseId,
        Guid VendorAId,
        Guid VendorBId,
        Guid CustomerAId,
        Guid CustomerBId,
        Guid ItemId,
        Guid PostedPurchaseReceiptId,
        Guid DraftPurchaseReceiptId,
        Guid PostedSalesInvoiceId,
        Guid DraftSalesInvoiceId);
}
