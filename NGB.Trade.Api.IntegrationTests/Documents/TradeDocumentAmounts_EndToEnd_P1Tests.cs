using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Runtime;
using Npgsql;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Documents;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeDocumentAmounts_EndToEnd_P1Tests(TradePostgresFixture fixture) : IAsyncLifetime
{
    private static readonly string[] AmountBearingDocumentTypes =
    [
        TradeCodes.PurchaseReceipt,
        TradeCodes.SalesInvoice,
        TradeCodes.CustomerPayment,
        TradeCodes.VendorPayment,
        TradeCodes.InventoryAdjustment,
        TradeCodes.CustomerReturn,
        TradeCodes.VendorReturn
    ];

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AmountBearingTradeDocuments_Metadata_ProjectsAmountField_And_PrefersItInLists()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        foreach (var typeCode in AmountBearingDocumentTypes)
        {
            var metadata = await documents.GetTypeMetadataAsync(typeCode, CancellationToken.None);

            metadata.Presentation.Should().NotBeNull($"{typeCode} should expose presentation metadata.");
            metadata.Presentation!.AmountField.Should().Be("amount");
            metadata.List!.Columns.Should().Contain(column => column.Key == "amount");
        }
    }

    [Fact]
    public async Task PurchaseReceipt_CreateAndUpdateDraft_RecalculatesPersistedAmount_And_ExposesItInPage()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.UnitOfMeasure, "Each");
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.PriceType, "Retail");
        var warehouseId = await CreateWarehouseAsync(catalogs, "Florida Fulfillment Center", "MIA", "100 Harbor Blvd, Miami, FL");
        var vendorId = await CreatePartyAsync(catalogs, "Northshore Supply", "V-100", isCustomer: false, isVendor: true);
        var itemAId = await CreateItemAsync(catalogs, unitOfMeasureId, retailPriceTypeId, "Extension Cord 25 ft", "EC-25");
        var itemBId = await CreateItemAsync(catalogs, unitOfMeasureId, retailPriceTypeId, "Workbench 60 in", "WB-600");

        var created = await documents.CreateDraftAsync(
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
                        ItemId: itemAId,
                        Quantity: 40m,
                        UnitCost: 8.82m,
                        LineAmount: 352.80m))),
            CancellationToken.None);

        ReadAmount(created).Should().Be(352.80m);
        await AssertPurchaseReceiptAmountAsync(created.Id, 352.80m);

        var updated = await documents.UpdateDraftAsync(
            TradeCodes.PurchaseReceipt,
            created.Id,
            TradePayloads.Payload(
                new
                {
                    notes = "Adjusted inbound replenishment"
                },
                TradePayloads.PurchaseReceiptLines(
                    new TradePayloads.PurchaseReceiptLineRow(
                        Ordinal: 1,
                        ItemId: itemAId,
                        Quantity: 20m,
                        UnitCost: 8.82m,
                        LineAmount: 176.40m),
                    new TradePayloads.PurchaseReceiptLineRow(
                        Ordinal: 2,
                        ItemId: itemBId,
                        Quantity: 3m,
                        UnitCost: 125.50m,
                        LineAmount: 376.50m))),
            CancellationToken.None);

        ReadAmount(updated).Should().Be(552.90m);
        await AssertPurchaseReceiptAmountAsync(updated.Id, 552.90m);

        var page = await documents.GetPageAsync(
            TradeCodes.PurchaseReceipt,
            new PageRequestDto(Offset: 0, Limit: 20, Search: null),
            CancellationToken.None);

        var listed = page.Items.Should().ContainSingle(x => x.Id == updated.Id).Subject;
        ReadAmount(listed).Should().Be(552.90m);
    }

    private async Task AssertPurchaseReceiptAmountAsync(Guid documentId, decimal expectedAmount)
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT amount FROM doc_trd_purchase_receipt WHERE document_id = @documentId;",
            conn);
        cmd.Parameters.AddWithValue("documentId", documentId);

        var scalar = await cmd.ExecuteScalarAsync(CancellationToken.None);
        scalar.Should().NotBeNull();
        Convert.ToDecimal(scalar).Should().Be(expectedAmount);
    }

    private static decimal ReadAmount(DocumentDto document)
    {
        document.Payload.Fields.Should().NotBeNull();
        document.Payload.Fields!.Should().ContainKey("amount");

        var value = document.Payload.Fields["amount"];
        value.ValueKind.Should().Be(JsonValueKind.Number);
        return value.GetDecimal();
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
        var page = await catalogs.GetPageAsync(catalogType, new PageRequestDto(0, 25, display), CancellationToken.None);
        return page.Items.Single(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase)).Id;
    }
}
