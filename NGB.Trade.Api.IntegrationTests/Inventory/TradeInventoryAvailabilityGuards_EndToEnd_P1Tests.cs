using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Tools.Exceptions;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Runtime;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Inventory;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeInventoryAvailabilityGuards_EndToEnd_P1Tests(TradePostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SalesInvoice_Rejects_When_Aggregated_Line_Quantity_Exceeds_OnHand()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var scenario = await SeedOutboundInventoryScenarioAsync(setup, catalogs, documents);

        var draft = await documents.CreateDraftAsync(
            TradeCodes.SalesInvoice,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-11",
                    customer_id = scenario.CustomerId,
                    warehouse_id = scenario.MainWarehouseId,
                    price_type_id = scenario.RetailPriceTypeId,
                    notes = "Rush contractor pickup"
                },
                TradePayloads.SalesInvoiceLines(
                    new TradePayloads.SalesInvoiceLineRow(1, scenario.ItemId, 6m, 20m, 12m, 120m),
                    new TradePayloads.SalesInvoiceLineRow(2, scenario.ItemId, 5m, 20m, 12m, 100m))),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.SalesInvoice, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
        ex.Which.Reason.Should().Contain("Insufficient inventory on hand as of 2026-04-11.");
        ex.Which.Reason.Should().Contain("Florida Fulfillment Center");
        ex.Which.Reason.Should().Contain("Cable Management Kit");
        ex.Which.Reason.Should().Contain("requested 11");
        ex.Which.Reason.Should().Contain("available 10");
    }

    [Fact]
    public async Task SalesInvoice_Rejects_When_OnHand_Comes_From_Prior_Month_Movements_Without_Projection_Snapshot()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var scenario = await SeedOutboundInventoryScenarioAsync(
            setup,
            catalogs,
            documents,
            purchaseDate: new DateOnly(2026, 3, 31));

        var draft = await documents.CreateDraftAsync(
            TradeCodes.SalesInvoice,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-11",
                    customer_id = scenario.CustomerId,
                    warehouse_id = scenario.MainWarehouseId,
                    price_type_id = scenario.RetailPriceTypeId,
                    notes = "Month boundary fulfillment"
                },
                TradePayloads.SalesInvoiceLines(
                    new TradePayloads.SalesInvoiceLineRow(1, scenario.ItemId, 11m, 20m, 12m, 220m))),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.SalesInvoice, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
        ex.Which.Reason.Should().Contain("Insufficient inventory on hand as of 2026-04-11.");
        ex.Which.Reason.Should().Contain("requested 11");
        ex.Which.Reason.Should().Contain("available 10");
    }

    [Fact]
    public async Task InventoryTransfer_Rejects_When_Source_Warehouse_Lacks_Stock()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var scenario = await SeedOutboundInventoryScenarioAsync(setup, catalogs, documents);

        var draft = await documents.CreateDraftAsync(
            TradeCodes.InventoryTransfer,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-11",
                    from_warehouse_id = scenario.MainWarehouseId,
                    to_warehouse_id = scenario.OverflowWarehouseId,
                    notes = "Emergency rebalancing"
                },
                TradePayloads.InventoryTransferLines(
                    new TradePayloads.InventoryTransferLineRow(1, scenario.ItemId, 11m))),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.InventoryTransfer, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
        ex.Which.Reason.Should().Contain("Insufficient inventory on hand as of 2026-04-11.");
        ex.Which.Reason.Should().Contain("Florida Fulfillment Center");
        ex.Which.Reason.Should().Contain("requested 11");
        ex.Which.Reason.Should().Contain("available 10");
    }

    [Fact]
    public async Task VendorReturn_Rejects_When_Warehouse_Lacks_Stock()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var scenario = await SeedOutboundInventoryScenarioAsync(setup, catalogs, documents);

        var draft = await documents.CreateDraftAsync(
            TradeCodes.VendorReturn,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-11",
                    vendor_id = scenario.VendorId,
                    warehouse_id = scenario.MainWarehouseId,
                    purchase_receipt_id = scenario.PurchaseReceiptId,
                    notes = "Over-shipped quantity dispute"
                },
                TradePayloads.VendorReturnLines(
                    new TradePayloads.VendorReturnLineRow(1, scenario.ItemId, 11m, 12m, 132m))),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.VendorReturn, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
        ex.Which.Reason.Should().Contain("Insufficient inventory on hand as of 2026-04-11.");
        ex.Which.Reason.Should().Contain("Florida Fulfillment Center");
        ex.Which.Reason.Should().Contain("requested 11");
        ex.Which.Reason.Should().Contain("available 10");
    }

    [Fact]
    public async Task InventoryAdjustment_Rejects_When_Negative_Delta_Exceeds_OnHand()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var scenario = await SeedOutboundInventoryScenarioAsync(setup, catalogs, documents);

        var draft = await documents.CreateDraftAsync(
            TradeCodes.InventoryAdjustment,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-11",
                    warehouse_id = scenario.MainWarehouseId,
                    reason_id = scenario.CountCorrectionReasonId,
                    notes = "Cycle count write-off"
                },
                TradePayloads.InventoryAdjustmentLines(
                    new TradePayloads.InventoryAdjustmentLineRow(1, scenario.ItemId, -11m, 12m, 132m))),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.InventoryAdjustment, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
        ex.Which.Reason.Should().Contain("Insufficient inventory on hand as of 2026-04-11.");
        ex.Which.Reason.Should().Contain("Florida Fulfillment Center");
        ex.Which.Reason.Should().Contain("requested 11");
        ex.Which.Reason.Should().Contain("available 10");
    }

    private static async Task<OutboundInventoryScenario> SeedOutboundInventoryScenarioAsync(
        ITradeSetupService setup,
        ICatalogService catalogs,
        IDocumentService documents,
        DateOnly? purchaseDate = null)
    {
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.UnitOfMeasure, "Each");
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.PriceType, "Retail");
        var countCorrectionReasonId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.InventoryAdjustmentReason, "Count Correction");

        var mainWarehouse = await catalogs.CreateAsync(
            TradeCodes.Warehouse,
            TradePayloads.Payload(new
            {
                display = "Florida Fulfillment Center",
                warehouse_code = "FLA",
                name = "Florida Fulfillment Center",
                address = "100 Harbor Blvd, Miami, FL",
                is_active = true
            }),
            CancellationToken.None);

        var overflowWarehouse = await catalogs.CreateAsync(
            TradeCodes.Warehouse,
            TradePayloads.Payload(new
            {
                display = "South Central Distribution Center",
                warehouse_code = "TXC",
                name = "South Central Distribution Center",
                address = "8200 Sterling St, Irving, TX",
                is_active = true
            }),
            CancellationToken.None);

        var vendor = await catalogs.CreateAsync(
            TradeCodes.Party,
            TradePayloads.Payload(new
            {
                display = "Northstar Distribution",
                name = "Northstar Distribution",
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
                display = "Bayview Stores",
                name = "Bayview Stores",
                is_customer = true,
                is_vendor = false,
                is_active = true,
                default_currency = "USD"
            }),
            CancellationToken.None);

        var item = await catalogs.CreateAsync(
            TradeCodes.Item,
            TradePayloads.Payload(new
            {
                display = "Cable Management Kit",
                name = "Cable Management Kit",
                sku = "CMK-100",
                unit_of_measure_id = unitOfMeasureId,
                default_sales_price_type_id = retailPriceTypeId,
                is_inventory_item = true,
                is_active = true
            }),
            CancellationToken.None);

        var purchaseDraft = await documents.CreateDraftAsync(
            TradeCodes.PurchaseReceipt,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = (purchaseDate ?? new DateOnly(2026, 4, 10)).ToString("yyyy-MM-dd"),
                    vendor_id = vendor.Id,
                    warehouse_id = mainWarehouse.Id,
                    notes = "Opening stock receipt"
                },
                TradePayloads.PurchaseReceiptLines(
                    new TradePayloads.PurchaseReceiptLineRow(1, item.Id, 10m, 12m, 120m))),
            CancellationToken.None);

        var purchasePosted = await documents.PostAsync(TradeCodes.PurchaseReceipt, purchaseDraft.Id, CancellationToken.None);

        return new OutboundInventoryScenario(
            mainWarehouse.Id,
            overflowWarehouse.Id,
            vendor.Id,
            customer.Id,
            item.Id,
            retailPriceTypeId,
            countCorrectionReasonId,
            purchasePosted.Id);
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

    private sealed record OutboundInventoryScenario(
        Guid MainWarehouseId,
        Guid OverflowWarehouseId,
        Guid VendorId,
        Guid CustomerId,
        Guid ItemId,
        Guid RetailPriceTypeId,
        Guid CountCorrectionReasonId,
        Guid PurchaseReceiptId);
}
