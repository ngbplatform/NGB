using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Runtime;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Inventory;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeInventoryCore_EndToEnd_P1Tests(TradePostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PurchaseReceipt_And_SalesInvoice_FeedInventoryReports_And_Accounting()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var definitions = scope.ServiceProvider.GetRequiredService<IReportDefinitionProvider>();
        var reports = scope.ServiceProvider.GetRequiredService<IReportEngine>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var balancesDefinition = await definitions.GetDefinitionAsync(TradeCodes.InventoryBalancesReport, CancellationToken.None);
        balancesDefinition.Name.Should().Be("Inventory Balances");
        balancesDefinition.Mode.Should().Be(ReportExecutionMode.Composable);
        var movementsDefinition = await definitions.GetDefinitionAsync(TradeCodes.InventoryMovementsReport, CancellationToken.None);
        movementsDefinition.Name.Should().Be("Inventory Movements");
        movementsDefinition.Mode.Should().Be(ReportExecutionMode.Composable);

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

        var item = await catalogs.CreateAsync(
            TradeCodes.Item,
            TradePayloads.Payload(new
            {
                display = "Demo Widget",
                name = "Demo Widget",
                sku = "DW-200",
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
                    warehouse_id = warehouse.Id,
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

        purchaseDraft.Number.Should().NotBeNullOrWhiteSpace();

        var purchasePosted = await documents.PostAsync(TradeCodes.PurchaseReceipt, purchaseDraft.Id, CancellationToken.None);
        purchasePosted.Status.Should().Be(DocumentStatus.Posted);

        var salesDraft = await documents.CreateDraftAsync(
            TradeCodes.SalesInvoice,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-11",
                    customer_id = customer.Id,
                    warehouse_id = warehouse.Id,
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

        salesDraft.Number.Should().NotBeNullOrWhiteSpace();

        var salesPosted = await documents.PostAsync(TradeCodes.SalesInvoice, salesDraft.Id, CancellationToken.None);
        salesPosted.Status.Should().Be(DocumentStatus.Posted);

        var purchaseEffects = await documents.GetEffectsAsync(TradeCodes.PurchaseReceipt, purchasePosted.Id, limit: 20, CancellationToken.None);
        purchaseEffects.AccountingEntries.Should().HaveCount(1);
        purchaseEffects.OperationalRegisterMovements.Should().HaveCount(1);
        purchaseEffects.AccountingEntries[0].DebitAccount.Code.Should().Be("1200");
        purchaseEffects.AccountingEntries[0].CreditAccount.Code.Should().Be("2000");
        purchaseEffects.AccountingEntries[0].Amount.Should().Be(120m);
        purchaseEffects.OperationalRegisterMovements[0].Resources.Should().ContainSingle(x => x.Code == "qty_in" && x.Value == 10m);
        purchaseEffects.OperationalRegisterMovements[0].Resources.Should().ContainSingle(x => x.Code == "qty_delta" && x.Value == 10m);

        var salesEffects = await documents.GetEffectsAsync(TradeCodes.SalesInvoice, salesPosted.Id, limit: 20, CancellationToken.None);
        salesEffects.AccountingEntries.Should().HaveCount(2);
        salesEffects.OperationalRegisterMovements.Should().HaveCount(1);
        salesEffects.AccountingEntries.Should().ContainSingle(x =>
            x.DebitAccount.Code == "1100"
            && x.CreditAccount.Code == "4000"
            && x.Amount == 80m);
        salesEffects.AccountingEntries.Should().ContainSingle(x =>
            x.DebitAccount.Code == "5000"
            && x.CreditAccount.Code == "1200"
            && x.Amount == 48m);
        salesEffects.OperationalRegisterMovements[0].Resources.Should().ContainSingle(x => x.Code == "qty_out" && x.Value == 4m);
        salesEffects.OperationalRegisterMovements[0].Resources.Should().ContainSingle(x => x.Code == "qty_delta" && x.Value == -4m);

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
                    ["as_of_utc"] = "2026-04-11"
                }),
            CancellationToken.None);

        balancesResponse.Total.Should().Be(1);
        balancesResponse.Sheet.Rows.Should().HaveCount(1);
        balancesResponse.Sheet.Rows[0].Cells[0].Display.Should().Be("Demo Widget");
        balancesResponse.Sheet.Rows[0].Cells[0].Action.Should().BeEquivalentTo(new ReportCellActionDto(
            ReportCellActionKinds.OpenCatalog,
            CatalogType: TradeCodes.Item,
            CatalogId: item.Id));
        balancesResponse.Sheet.Rows[0].Cells[1].Display.Should().Be("Main Warehouse — 100 Harbor Blvd, Miami, FL");
        balancesResponse.Sheet.Rows[0].Cells[1].Action.Should().BeEquivalentTo(new ReportCellActionDto(
            ReportCellActionKinds.OpenCatalog,
            CatalogType: TradeCodes.Warehouse,
            CatalogId: warehouse.Id));
        balancesResponse.Sheet.Rows[0].Cells[2].Display.Should().Be("6");

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

        movementsResponse.Total.Should().Be(2);
        movementsResponse.Sheet.Rows.Should().HaveCount(2);
        movementsResponse.Sheet.Rows[0].Cells[1].Display.Should().Be("Demo Widget");
        movementsResponse.Sheet.Rows[0].Cells[1].Action.Should().BeEquivalentTo(new ReportCellActionDto(
            ReportCellActionKinds.OpenCatalog,
            CatalogType: TradeCodes.Item,
            CatalogId: item.Id));
        movementsResponse.Sheet.Rows[0].Cells[2].Display.Should().Be("Main Warehouse — 100 Harbor Blvd, Miami, FL");
        movementsResponse.Sheet.Rows[0].Cells[2].Action.Should().BeEquivalentTo(new ReportCellActionDto(
            ReportCellActionKinds.OpenCatalog,
            CatalogType: TradeCodes.Warehouse,
            CatalogId: warehouse.Id));
        movementsResponse.Sheet.Rows[0].Cells[3].Display.Should().Be(purchasePosted.Display);
        movementsResponse.Sheet.Rows[0].Cells[3].Action!.DocumentType.Should().Be(TradeCodes.PurchaseReceipt);
        movementsResponse.Sheet.Rows[0].Cells[3].Action!.DocumentId.Should().Be(purchasePosted.Id);
        movementsResponse.Sheet.Rows[0].Cells[4].Display.Should().Be("10");
        movementsResponse.Sheet.Rows[0].Cells[6].Display.Should().Be("10");
        movementsResponse.Sheet.Rows[1].Cells[3].Display.Should().Be(salesPosted.Display);
        movementsResponse.Sheet.Rows[1].Cells[3].Action!.DocumentType.Should().Be(TradeCodes.SalesInvoice);
        movementsResponse.Sheet.Rows[1].Cells[3].Action!.DocumentId.Should().Be(salesPosted.Id);
        movementsResponse.Sheet.Rows[1].Cells[5].Display.Should().Be("4");
        movementsResponse.Sheet.Rows[1].Cells[6].Display.Should().Be("-4");

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

        journalResponse.Sheet.Rows.Should().HaveCount(3);
        journalResponse.HasMore.Should().BeFalse();
        journalResponse.Sheet.Rows.Should().Contain(row =>
            row.Cells[2].Display!.Contains("Inventory", StringComparison.Ordinal)
            && row.Cells[4].Display!.Contains("Accounts Payable", StringComparison.Ordinal)
            && row.Cells[6].Display == "120");
        journalResponse.Sheet.Rows.Should().Contain(row =>
            row.Cells[2].Display!.Contains("Accounts Receivable", StringComparison.Ordinal)
            && row.Cells[4].Display!.Contains("Sales Revenue", StringComparison.Ordinal)
            && row.Cells[6].Display == "80");
        journalResponse.Sheet.Rows.Should().Contain(row =>
            row.Cells[2].Display!.Contains("Cost of Goods Sold", StringComparison.Ordinal)
            && row.Cells[4].Display!.Contains("Inventory", StringComparison.Ordinal)
            && row.Cells[6].Display == "48");

        var salesAfterUnpost = await documents.UnpostAsync(TradeCodes.SalesInvoice, salesPosted.Id, CancellationToken.None);
        salesAfterUnpost.Status.Should().Be(DocumentStatus.Draft);

        var salesEffectsAfterUnpost = await documents.GetEffectsAsync(TradeCodes.SalesInvoice, salesPosted.Id, limit: 20, CancellationToken.None);
        salesEffectsAfterUnpost.AccountingEntries.Should().BeEmpty();
        salesEffectsAfterUnpost.OperationalRegisterMovements.Should().BeEmpty();

        var balancesAfterUnpost = await reports.ExecuteAsync(
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
                    ["as_of_utc"] = "2026-04-11"
                }),
            CancellationToken.None);

        balancesAfterUnpost.Total.Should().Be(1);
        balancesAfterUnpost.Sheet.Rows.Single().Cells[2].Display.Should().Be("10");
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
