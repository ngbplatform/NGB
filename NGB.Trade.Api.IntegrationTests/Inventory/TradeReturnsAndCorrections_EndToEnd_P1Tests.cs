using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Runtime;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Inventory;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeReturnsAndCorrections_EndToEnd_P1Tests(TradePostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Returns_Transfers_And_Adjustments_UpdateInventoryAndAccounting()
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
        var countCorrectionReasonId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.InventoryAdjustmentReason, "Count Correction");

        var mainWarehouse = await catalogs.CreateAsync(
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

        var overflowWarehouse = await catalogs.CreateAsync(
            TradeCodes.Warehouse,
            TradePayloads.Payload(new
            {
                display = "Overflow Warehouse",
                warehouse_code = "OVERFLOW",
                name = "Overflow Warehouse",
                address = "200 Harbor Blvd, Miami, FL",
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

        var item = await catalogs.CreateAsync(
            TradeCodes.Item,
            TradePayloads.Payload(new
            {
                display = "Demo Widget",
                name = "Demo Widget",
                sku = "DW-400",
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
                    document_date_utc = "2026-04-10",
                    vendor_id = vendor.Id,
                    warehouse_id = mainWarehouse.Id,
                    notes = "Initial stocking"
                },
                TradePayloads.PurchaseReceiptLines(
                    new TradePayloads.PurchaseReceiptLineRow(
                        Ordinal: 1,
                        ItemId: item.Id,
                        Quantity: 10m,
                        UnitCost: 12m,
                        LineAmount: 120m))),
            CancellationToken.None);

        var purchasePosted = await documents.PostAsync(TradeCodes.PurchaseReceipt, purchaseDraft.Id, CancellationToken.None);

        var salesDraft = await documents.CreateDraftAsync(
            TradeCodes.SalesInvoice,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-11",
                    customer_id = customer.Id,
                    warehouse_id = mainWarehouse.Id,
                    price_type_id = retailPriceTypeId,
                    notes = "Customer sale"
                },
                TradePayloads.SalesInvoiceLines(
                    new TradePayloads.SalesInvoiceLineRow(
                        Ordinal: 1,
                        ItemId: item.Id,
                        Quantity: 4m,
                        UnitPrice: 20m,
                        UnitCost: 12m,
                        LineAmount: 80m))),
            CancellationToken.None);

        var salesPosted = await documents.PostAsync(TradeCodes.SalesInvoice, salesDraft.Id, CancellationToken.None);

        var customerReturnDraft = await documents.CreateDraftAsync(
            TradeCodes.CustomerReturn,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-12",
                    customer_id = customer.Id,
                    warehouse_id = mainWarehouse.Id,
                    sales_invoice_id = salesPosted.Id,
                    notes = "Returned by customer"
                },
                TradePayloads.CustomerReturnLines(
                    new TradePayloads.CustomerReturnLineRow(
                        Ordinal: 1,
                        ItemId: item.Id,
                        Quantity: 1m,
                        UnitPrice: 20m,
                        UnitCost: 12m,
                        LineAmount: 20m))),
            CancellationToken.None);

        var customerReturnPosted = await documents.PostAsync(TradeCodes.CustomerReturn, customerReturnDraft.Id, CancellationToken.None);

        var customerReturnGraph = await documents.GetRelationshipGraphAsync(
            TradeCodes.CustomerReturn,
            customerReturnPosted.Id,
            depth: 1,
            maxNodes: 20,
            CancellationToken.None);
        customerReturnGraph.Nodes.Select(x => x.EntityId).Should().BeEquivalentTo([customerReturnPosted.Id, salesPosted.Id]);
        customerReturnGraph.Edges.Should().ContainSingle(x =>
            x.FromNodeId == NodeId(TradeCodes.CustomerReturn, customerReturnPosted.Id)
            && x.ToNodeId == NodeId(TradeCodes.SalesInvoice, salesPosted.Id)
            && x.RelationshipType == "based_on");

        var vendorReturnDraft = await documents.CreateDraftAsync(
            TradeCodes.VendorReturn,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-13",
                    vendor_id = vendor.Id,
                    warehouse_id = mainWarehouse.Id,
                    purchase_receipt_id = purchasePosted.Id,
                    notes = "Returned to vendor"
                },
                TradePayloads.VendorReturnLines(
                    new TradePayloads.VendorReturnLineRow(
                        Ordinal: 1,
                        ItemId: item.Id,
                        Quantity: 2m,
                        UnitCost: 12m,
                        LineAmount: 24m))),
            CancellationToken.None);

        var vendorReturnPosted = await documents.PostAsync(TradeCodes.VendorReturn, vendorReturnDraft.Id, CancellationToken.None);

        var vendorReturnGraph = await documents.GetRelationshipGraphAsync(
            TradeCodes.VendorReturn,
            vendorReturnPosted.Id,
            depth: 1,
            maxNodes: 20,
            CancellationToken.None);
        vendorReturnGraph.Nodes.Select(x => x.EntityId).Should().BeEquivalentTo([vendorReturnPosted.Id, purchasePosted.Id]);
        vendorReturnGraph.Edges.Should().ContainSingle(x =>
            x.FromNodeId == NodeId(TradeCodes.VendorReturn, vendorReturnPosted.Id)
            && x.ToNodeId == NodeId(TradeCodes.PurchaseReceipt, purchasePosted.Id)
            && x.RelationshipType == "based_on");

        var transferDraft = await documents.CreateDraftAsync(
            TradeCodes.InventoryTransfer,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-14",
                    from_warehouse_id = mainWarehouse.Id,
                    to_warehouse_id = overflowWarehouse.Id,
                    notes = "Move excess stock"
                },
                TradePayloads.InventoryTransferLines(
                    new TradePayloads.InventoryTransferLineRow(
                        Ordinal: 1,
                        ItemId: item.Id,
                        Quantity: 3m))),
            CancellationToken.None);

        var transferPosted = await documents.PostAsync(TradeCodes.InventoryTransfer, transferDraft.Id, CancellationToken.None);

        var adjustmentDraft = await documents.CreateDraftAsync(
            TradeCodes.InventoryAdjustment,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-15",
                    warehouse_id = overflowWarehouse.Id,
                    reason_id = countCorrectionReasonId,
                    notes = "Count found extra units"
                },
                TradePayloads.InventoryAdjustmentLines(
                    new TradePayloads.InventoryAdjustmentLineRow(
                        Ordinal: 1,
                        ItemId: item.Id,
                        QuantityDelta: 2m,
                        UnitCost: 12m,
                        LineAmount: 24m))),
            CancellationToken.None);

        var adjustmentPosted = await documents.PostAsync(TradeCodes.InventoryAdjustment, adjustmentDraft.Id, CancellationToken.None);

        var customerReturnEffects = await documents.GetEffectsAsync(TradeCodes.CustomerReturn, customerReturnPosted.Id, limit: 20, CancellationToken.None);
        customerReturnEffects.AccountingEntries.Should().HaveCount(2);
        customerReturnEffects.OperationalRegisterMovements.Should().HaveCount(1);
        customerReturnEffects.AccountingEntries.Should().ContainSingle(x =>
            x.DebitAccount.Code == "4000"
            && x.CreditAccount.Code == "1100"
            && x.Amount == 20m);
        customerReturnEffects.AccountingEntries.Should().ContainSingle(x =>
            x.DebitAccount.Code == "1200"
            && x.CreditAccount.Code == "5000"
            && x.Amount == 12m);
        customerReturnEffects.OperationalRegisterMovements[0].Resources.Should().ContainSingle(x => x.Code == "qty_in" && x.Value == 1m);
        customerReturnEffects.OperationalRegisterMovements[0].Resources.Should().ContainSingle(x => x.Code == "qty_delta" && x.Value == 1m);

        var vendorReturnEffects = await documents.GetEffectsAsync(TradeCodes.VendorReturn, vendorReturnPosted.Id, limit: 20, CancellationToken.None);
        vendorReturnEffects.AccountingEntries.Should().HaveCount(1);
        vendorReturnEffects.OperationalRegisterMovements.Should().HaveCount(1);
        vendorReturnEffects.AccountingEntries[0].DebitAccount.Code.Should().Be("2000");
        vendorReturnEffects.AccountingEntries[0].CreditAccount.Code.Should().Be("1200");
        vendorReturnEffects.AccountingEntries[0].Amount.Should().Be(24m);
        vendorReturnEffects.OperationalRegisterMovements[0].Resources.Should().ContainSingle(x => x.Code == "qty_out" && x.Value == 2m);
        vendorReturnEffects.OperationalRegisterMovements[0].Resources.Should().ContainSingle(x => x.Code == "qty_delta" && x.Value == -2m);

        var transferEffects = await documents.GetEffectsAsync(TradeCodes.InventoryTransfer, transferPosted.Id, limit: 20, CancellationToken.None);
        transferEffects.AccountingEntries.Should().BeEmpty();
        transferEffects.OperationalRegisterMovements.Should().HaveCount(2);
        transferEffects.OperationalRegisterMovements.Should().Contain(x =>
            x.Resources.Any(r => r.Code == "qty_out" && r.Value == 3m)
            && x.Resources.Any(r => r.Code == "qty_delta" && r.Value == -3m));
        transferEffects.OperationalRegisterMovements.Should().Contain(x =>
            x.Resources.Any(r => r.Code == "qty_in" && r.Value == 3m)
            && x.Resources.Any(r => r.Code == "qty_delta" && r.Value == 3m));

        var adjustmentEffects = await documents.GetEffectsAsync(TradeCodes.InventoryAdjustment, adjustmentPosted.Id, limit: 20, CancellationToken.None);
        adjustmentEffects.AccountingEntries.Should().HaveCount(1);
        adjustmentEffects.OperationalRegisterMovements.Should().HaveCount(1);
        adjustmentEffects.AccountingEntries[0].DebitAccount.Code.Should().Be("1200");
        adjustmentEffects.AccountingEntries[0].CreditAccount.Code.Should().Be("5200");
        adjustmentEffects.AccountingEntries[0].Amount.Should().Be(24m);
        adjustmentEffects.OperationalRegisterMovements[0].Resources.Should().ContainSingle(x => x.Code == "qty_in" && x.Value == 2m);
        adjustmentEffects.OperationalRegisterMovements[0].Resources.Should().ContainSingle(x => x.Code == "qty_delta" && x.Value == 2m);

        var balancesResponse = await reports.ExecuteAsync(
            TradeCodes.InventoryBalancesReport,
            new ReportExecutionRequestDto(
                DisablePaging: true,
                Layout: new ReportLayoutDto(
                    Measures:
                    [
                        new ReportMeasureSelectionDto("quantity_on_hand")
                    ],
                    DetailFields:
                    [
                        "item_display",
                        "warehouse_display"
                    ],
                    Sorts:
                    [
                        new ReportSortDto("item_display"),
                        new ReportSortDto("warehouse_display")
                    ],
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: false),
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-04-15"
                }),
            CancellationToken.None);

        balancesResponse.Total.Should().Be(2);
        balancesResponse.Sheet.Rows.Should().HaveCount(2);
        balancesResponse.Sheet.Rows.Should().Contain(row =>
            row.Cells[0].Display == "Demo Widget"
            && row.Cells[1].Display == "Main Warehouse — 100 Harbor Blvd, Miami, FL"
            && row.Cells[2].Display == "2");
        balancesResponse.Sheet.Rows.Should().Contain(row =>
            row.Cells[0].Display == "Demo Widget"
            && row.Cells[1].Display == "Overflow Warehouse — 200 Harbor Blvd, Miami, FL"
            && row.Cells[2].Display == "5");

        var movementsResponse = await reports.ExecuteAsync(
            TradeCodes.InventoryMovementsReport,
            new ReportExecutionRequestDto(
                DisablePaging: true,
                Layout: new ReportLayoutDto(
                    Measures:
                    [
                        new ReportMeasureSelectionDto("qty_in"),
                        new ReportMeasureSelectionDto("qty_out"),
                        new ReportMeasureSelectionDto("qty_delta")
                    ],
                    DetailFields:
                    [
                        "occurred_at_utc",
                        "item_display",
                        "warehouse_display",
                        "document_display"
                    ],
                    Sorts:
                    [
                        new ReportSortDto("occurred_at_utc")
                    ],
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: false),
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-04-01",
                    ["to_utc"] = "2026-04-30"
                }),
            CancellationToken.None);

        movementsResponse.Total.Should().Be(7);
        movementsResponse.Sheet.Rows.Should().HaveCount(7);

        var journalResponse = await reports.ExecuteAsync(
            "accounting.general_journal",
            new ReportExecutionRequestDto(
                DisablePaging: true,
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-04-01",
                    ["to_utc"] = "2026-04-30"
                }),
            CancellationToken.None);

        journalResponse.Sheet.Rows.Should().HaveCount(7);
        journalResponse.Sheet.Rows.Should().Contain(row =>
            row.Cells[2].Display!.Contains("Sales Revenue", StringComparison.Ordinal)
            && row.Cells[4].Display!.Contains("Accounts Receivable", StringComparison.Ordinal)
            && row.Cells[6].Display == "20");
        journalResponse.Sheet.Rows.Should().Contain(row =>
            row.Cells[2].Display!.Contains("Inventory", StringComparison.Ordinal)
            && row.Cells[4].Display!.Contains("Cost of Goods Sold", StringComparison.Ordinal)
            && row.Cells[6].Display == "12");
        journalResponse.Sheet.Rows.Should().Contain(row =>
            row.Cells[2].Display!.Contains("Accounts Payable", StringComparison.Ordinal)
            && row.Cells[4].Display!.Contains("Inventory", StringComparison.Ordinal)
            && row.Cells[6].Display == "24");
        journalResponse.Sheet.Rows.Should().Contain(row =>
            row.Cells[2].Display!.Contains("Inventory", StringComparison.Ordinal)
            && row.Cells[4].Display!.Contains("Inventory Adjustment", StringComparison.Ordinal)
            && row.Cells[6].Display == "24");
    }

    [Fact]
    public async Task InventoryTransfer_Rejects_SameWarehouse()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

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

        var item = await catalogs.CreateAsync(
            TradeCodes.Item,
            TradePayloads.Payload(new
            {
                display = "Demo Widget",
                name = "Demo Widget",
                sku = "DW-401",
                unit_of_measure_id = unitOfMeasureId,
                default_sales_price_type_id = retailPriceTypeId,
                is_inventory_item = true,
                is_active = true
            }),
            CancellationToken.None);

        var draft = await documents.CreateDraftAsync(
            TradeCodes.InventoryTransfer,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-14",
                    from_warehouse_id = warehouse.Id,
                    to_warehouse_id = warehouse.Id,
                    notes = "Invalid move"
                },
                TradePayloads.InventoryTransferLines(
                    new TradePayloads.InventoryTransferLineRow(
                        Ordinal: 1,
                        ItemId: item.Id,
                        Quantity: 1m))),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.InventoryTransfer, draft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("to_warehouse_id");
        ex.Which.Reason.Should().Be("From Warehouse and To Warehouse must be different.");
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

    private static string NodeId(string typeCode, Guid id) => $"doc:{typeCode}:{id}";
}
