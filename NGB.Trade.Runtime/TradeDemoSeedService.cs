using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Tools.Exceptions;
using NGB.Trade.Contracts;

namespace NGB.Trade.Runtime;

public sealed class TradeDemoSeedService(
    ITradeSetupService setup,
    ICatalogService catalogs,
    IDocumentService documents,
    TimeProvider timeProvider)
    : ITradeDemoSeedService
{
    public async Task<TradeDemoSeedResult> EnsureDemoAsync(CancellationToken ct = default)
    {
        await setup.EnsureDefaultsAsync(ct);

        var todayUtc = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(TradeCodes.PriceType, "Retail", ct);
        var net30TermsId = await GetCatalogIdByDisplayAsync(TradeCodes.PaymentTerms, "Net 30", ct);
        var dueOnReceiptTermsId = await GetCatalogIdByDisplayAsync(TradeCodes.PaymentTerms, "Due on Receipt", ct);
        var countCorrectionReasonId = await GetCatalogIdByDisplayAsync(TradeCodes.InventoryAdjustmentReason, "Count Correction", ct);
        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(TradeCodes.UnitOfMeasure, "Each", ct);

        var mainWarehouse = await EnsureCatalogAsync(
            TradeCodes.Warehouse,
            "Main Warehouse",
            Payload(new
            {
                display = "Main Warehouse",
                warehouse_code = "MAIN",
                name = "Main Warehouse",
                address = "100 Harbor Blvd, Miami, FL",
                is_active = true
            }),
            ct,
            matchField: "warehouse_code",
            matchValue: "MAIN");

        var overflowWarehouse = await EnsureCatalogAsync(
            TradeCodes.Warehouse,
            "Overflow Warehouse",
            Payload(new
            {
                display = "Overflow Warehouse",
                warehouse_code = "OVERFLOW",
                name = "Overflow Warehouse",
                address = "250 Commerce Way, Miami, FL",
                is_active = true
            }),
            ct,
            matchField: "warehouse_code",
            matchValue: "OVERFLOW");

        var acmeSupply = await EnsureCatalogAsync(
            TradeCodes.Party,
            "Acme Supply",
            Payload(new
            {
                display = "Acme Supply",
                party_number = "V-100",
                name = "Acme Supply",
                legal_name = "Acme Supply LLC",
                email = "orders@acmesupply.example",
                phone = "+1-305-555-0100",
                billing_address = "500 Supplier Ave, Miami, FL",
                shipping_address = "500 Supplier Ave, Miami, FL",
                payment_terms_id = dueOnReceiptTermsId,
                default_currency = TradeCodes.DefaultCurrency,
                is_customer = false,
                is_vendor = true,
                is_active = true
            }),
            ct,
            matchField: "party_number",
            matchValue: "V-100");

        var globalIndustrial = await EnsureCatalogAsync(
            TradeCodes.Party,
            "Global Industrial",
            Payload(new
            {
                display = "Global Industrial",
                party_number = "V-200",
                name = "Global Industrial",
                legal_name = "Global Industrial Inc.",
                email = "sales@globalindustrial.example",
                phone = "+1-312-555-0120",
                billing_address = "900 Wholesale Pkwy, Chicago, IL",
                shipping_address = "900 Wholesale Pkwy, Chicago, IL",
                payment_terms_id = net30TermsId,
                default_currency = TradeCodes.DefaultCurrency,
                is_customer = false,
                is_vendor = true,
                is_active = true
            }),
            ct,
            matchField: "party_number",
            matchValue: "V-200");

        var northwindRetail = await EnsureCatalogAsync(
            TradeCodes.Party,
            "Northwind Retail",
            Payload(new
            {
                display = "Northwind Retail",
                party_number = "C-100",
                name = "Northwind Retail",
                legal_name = "Northwind Retail LLC",
                email = "ap@northwind.example",
                phone = "+1-212-555-0140",
                billing_address = "120 Retail Row, New York, NY",
                shipping_address = "120 Retail Row, New York, NY",
                payment_terms_id = net30TermsId,
                default_currency = TradeCodes.DefaultCurrency,
                is_customer = true,
                is_vendor = false,
                is_active = true
            }),
            ct,
            matchField: "party_number",
            matchValue: "C-100");

        var bayviewStores = await EnsureCatalogAsync(
            TradeCodes.Party,
            "Bayview Stores",
            Payload(new
            {
                display = "Bayview Stores",
                party_number = "C-200",
                name = "Bayview Stores",
                legal_name = "Bayview Stores Corp.",
                email = "buyers@bayview.example",
                phone = "+1-415-555-0160",
                billing_address = "44 Market Plaza, San Francisco, CA",
                shipping_address = "44 Market Plaza, San Francisco, CA",
                payment_terms_id = net30TermsId,
                default_currency = TradeCodes.DefaultCurrency,
                is_customer = true,
                is_vendor = false,
                is_active = true
            }),
            ct,
            matchField: "party_number",
            matchValue: "C-200");

        var alphaWidget = await EnsureCatalogAsync(
            TradeCodes.Item,
            "Alpha Widget",
            Payload(new
            {
                display = "Alpha Widget",
                name = "Alpha Widget",
                sku = "AW-100",
                unit_of_measure_id = unitOfMeasureId,
                item_type = "Inventory",
                is_inventory_item = true,
                default_sales_price_type_id = retailPriceTypeId,
                is_active = true
            }),
            ct);

        var bravoGadget = await EnsureCatalogAsync(
            TradeCodes.Item,
            "Bravo Gadget",
            Payload(new
            {
                display = "Bravo Gadget",
                name = "Bravo Gadget",
                sku = "BG-200",
                unit_of_measure_id = unitOfMeasureId,
                item_type = "Inventory",
                is_inventory_item = true,
                default_sales_price_type_id = retailPriceTypeId,
                is_active = true
            }),
            ct);

        var charlieCable = await EnsureCatalogAsync(
            TradeCodes.Item,
            "Charlie Cable",
            Payload(new
            {
                display = "Charlie Cable",
                name = "Charlie Cable",
                sku = "CC-300",
                unit_of_measure_id = unitOfMeasureId,
                item_type = "Inventory",
                is_inventory_item = true,
                default_sales_price_type_id = retailPriceTypeId,
                is_active = true
            }),
            ct);

        if (await HasAnyOperationalTradeDocumentsAsync(ct))
        {
            return new TradeDemoSeedResult(
                AsOfUtc: todayUtc,
                WarehousesEnsured: 2,
                PartnersEnsured: 4,
                ItemsEnsured: 3,
                DocumentsCreated: 0,
                SeededOperationalData: false);
        }

        var purchaseDate1 = InCurrentMonth(todayUtc, 2);
        var purchaseDate2 = InCurrentMonth(todayUtc, 3);
        var priceUpdateDate = InCurrentMonth(todayUtc, 4);
        var salesDate1 = InCurrentMonth(todayUtc, 5);
        var salesDate2 = InCurrentMonth(todayUtc, 6);
        var customerPaymentDate = InCurrentMonth(todayUtc, 7);
        var vendorPaymentDate = InCurrentMonth(todayUtc, 8);
        var transferDate = InCurrentMonth(todayUtc, 9);
        var correctionDate = todayUtc;

        var documentsCreated = 0;

        await CreateAndPostAsync(
            TradeCodes.ItemPriceUpdate,
            Payload(
                new
                {
                    effective_date = priceUpdateDate.ToString("yyyy-MM-dd"),
                    notes = "Initial retail price list"
                },
                ItemPriceUpdateLines(
                    new ItemPriceSeedLine(1, alphaWidget.Id, retailPriceTypeId, TradeCodes.DefaultCurrency, 10m),
                    new ItemPriceSeedLine(2, bravoGadget.Id, retailPriceTypeId, TradeCodes.DefaultCurrency, 8m),
                    new ItemPriceSeedLine(3, charlieCable.Id, retailPriceTypeId, TradeCodes.DefaultCurrency, 2.5m))),
            ct);
        documentsCreated++;

        var purchaseReceipt1 = await CreateAndPostAsync(
            TradeCodes.PurchaseReceipt,
            Payload(
                new
                {
                    document_date_utc = purchaseDate1.ToString("yyyy-MM-dd"),
                    vendor_id = acmeSupply.Id,
                    warehouse_id = mainWarehouse.Id,
                    notes = "Initial stock from Acme Supply"
                },
                PurchaseReceiptLines(
                    new PurchaseReceiptSeedLine(1, alphaWidget.Id, 40m, 5m, 200m),
                    new PurchaseReceiptSeedLine(2, bravoGadget.Id, 60m, 3m, 180m))),
            ct);
        documentsCreated++;

        var purchaseReceipt2 = await CreateAndPostAsync(
            TradeCodes.PurchaseReceipt,
            Payload(
                new
                {
                    document_date_utc = purchaseDate2.ToString("yyyy-MM-dd"),
                    vendor_id = globalIndustrial.Id,
                    warehouse_id = mainWarehouse.Id,
                    notes = "Cable replenishment from Global Industrial"
                },
                PurchaseReceiptLines(
                    new PurchaseReceiptSeedLine(1, charlieCable.Id, 100m, 1.2m, 120m))),
            ct);
        documentsCreated++;

        var salesInvoice1 = await CreateAndPostAsync(
            TradeCodes.SalesInvoice,
            Payload(
                new
                {
                    document_date_utc = salesDate1.ToString("yyyy-MM-dd"),
                    customer_id = northwindRetail.Id,
                    warehouse_id = mainWarehouse.Id,
                    price_type_id = retailPriceTypeId,
                    notes = "Northwind replenishment"
                },
                SalesInvoiceLines(
                    new SalesInvoiceSeedLine(1, alphaWidget.Id, 8m, 10m, 5m, 80m),
                    new SalesInvoiceSeedLine(2, bravoGadget.Id, 10m, 8m, 3m, 80m))),
            ct);
        documentsCreated++;

        var salesInvoice2 = await CreateAndPostAsync(
            TradeCodes.SalesInvoice,
            Payload(
                new
                {
                    document_date_utc = salesDate2.ToString("yyyy-MM-dd"),
                    customer_id = bayviewStores.Id,
                    warehouse_id = mainWarehouse.Id,
                    price_type_id = retailPriceTypeId,
                    notes = "Bayview mixed order"
                },
                SalesInvoiceLines(
                    new SalesInvoiceSeedLine(1, charlieCable.Id, 20m, 2.5m, 1.2m, 50m),
                    new SalesInvoiceSeedLine(2, bravoGadget.Id, 5m, 8m, 3m, 40m))),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            TradeCodes.CustomerPayment,
            Payload(new
            {
                document_date_utc = customerPaymentDate.ToString("yyyy-MM-dd"),
                customer_id = northwindRetail.Id,
                sales_invoice_id = salesInvoice1.Id,
                amount = 160m,
                notes = "Northwind payment"
            }),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            TradeCodes.VendorPayment,
            Payload(new
            {
                document_date_utc = vendorPaymentDate.ToString("yyyy-MM-dd"),
                vendor_id = acmeSupply.Id,
                purchase_receipt_id = purchaseReceipt1.Id,
                amount = 380m,
                notes = "Acme settlement"
            }),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            TradeCodes.InventoryTransfer,
            Payload(
                new
                {
                    document_date_utc = transferDate.ToString("yyyy-MM-dd"),
                    from_warehouse_id = mainWarehouse.Id,
                    to_warehouse_id = overflowWarehouse.Id,
                    notes = "Rebalance fast movers"
                },
                InventoryTransferLines(
                    new InventoryTransferSeedLine(1, bravoGadget.Id, 10m),
                    new InventoryTransferSeedLine(2, charlieCable.Id, 20m))),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            TradeCodes.InventoryAdjustment,
            Payload(
                new
                {
                    document_date_utc = correctionDate.ToString("yyyy-MM-dd"),
                    warehouse_id = overflowWarehouse.Id,
                    reason_id = countCorrectionReasonId,
                    notes = "Cycle count correction"
                },
                InventoryAdjustmentLines(
                    new InventoryAdjustmentSeedLine(1, charlieCable.Id, 3m, 1.2m, 3.6m))),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            TradeCodes.CustomerReturn,
            Payload(
                new
                {
                    document_date_utc = correctionDate.ToString("yyyy-MM-dd"),
                    customer_id = northwindRetail.Id,
                    warehouse_id = mainWarehouse.Id,
                    sales_invoice_id = salesInvoice1.Id,
                    notes = "Northwind partial return"
                },
                CustomerReturnLines(
                    new CustomerReturnSeedLine(1, alphaWidget.Id, 1m, 10m, 5m, 10m))),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            TradeCodes.VendorReturn,
            Payload(
                new
                {
                    document_date_utc = correctionDate.ToString("yyyy-MM-dd"),
                    vendor_id = acmeSupply.Id,
                    warehouse_id = mainWarehouse.Id,
                    purchase_receipt_id = purchaseReceipt1.Id,
                    notes = "Return defective gadgets"
                },
                VendorReturnLines(
                    new VendorReturnSeedLine(1, bravoGadget.Id, 4m, 3m, 12m))),
            ct);
        documentsCreated++;

        return new TradeDemoSeedResult(
            AsOfUtc: todayUtc,
            WarehousesEnsured: 2,
            PartnersEnsured: 4,
            ItemsEnsured: 3,
            DocumentsCreated: documentsCreated,
            SeededOperationalData: true);
    }

    private async Task<bool> HasAnyOperationalTradeDocumentsAsync(CancellationToken ct)
    {
        foreach (var documentType in DemoDocumentTypes)
        {
            var page = await documents.GetPageAsync(
                documentType,
                new PageRequestDto(Offset: 0, Limit: 1, Search: null),
                ct);

            if (page.Total.GetValueOrDefault(page.Items.Count) > 0)
                return true;
        }

        return false;
    }

    private async Task<CatalogItemDto> EnsureCatalogAsync(
        string catalogType,
        string display,
        RecordPayload payload,
        CancellationToken ct,
        string? matchField = null,
        string? matchValue = null)
    {
        var page = await catalogs.GetPageAsync(
            catalogType,
            new PageRequestDto(
                Offset: 0,
                Limit: 200,
                Search: string.IsNullOrWhiteSpace(matchField) ? display : null),
            ct);

        var matches = page.Items
            .Where(x =>
                string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase)
                || CatalogPayloadFieldEquals(x, matchField, matchValue))
            .ToArray();

        if (matches.Length > 1)
            throw new NgbConfigurationViolationException($"Multiple '{catalogType}' records exist for display '{display}'.");

        if (matches.Length == 1)
            return await catalogs.UpdateAsync(catalogType, matches[0].Id, payload, ct);

        return await catalogs.CreateAsync(catalogType, payload, ct);
    }

    private static bool CatalogPayloadFieldEquals(
        CatalogItemDto item,
        string? field,
        string? expected)
    {
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(expected))
            return false;

        if (item.Payload.Fields is null || !item.Payload.Fields.TryGetValue(field, out var value))
            return false;

        return string.Equals(value.ToString(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> GetCatalogIdByDisplayAsync(string catalogType, string display, CancellationToken ct)
    {
        var page = await catalogs.GetPageAsync(
            catalogType,
            new PageRequestDto(Offset: 0, Limit: 50, Search: display),
            ct);

        var matches = page.Items
            .Where(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0].Id,
            0 => throw new NgbConfigurationViolationException($"Default '{catalogType}' record '{display}' was not found."),
            _ => throw new NgbConfigurationViolationException($"Multiple '{catalogType}' records exist for display '{display}'.")
        };
    }

    private async Task<DocumentDto> CreateAndPostAsync(
        string documentType,
        RecordPayload payload,
        CancellationToken ct)
    {
        var draft = await documents.CreateDraftAsync(documentType, payload, ct);
        return await documents.PostAsync(documentType, draft.Id, ct);
    }

    private static DateOnly InCurrentMonth(DateOnly todayUtc, int preferredDay)
    {
        var day = Math.Max(1, Math.Min(preferredDay, todayUtc.Day));
        return new DateOnly(todayUtc.Year, todayUtc.Month, day);
    }

    private static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var element = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value;
        }

        return new RecordPayload(dict, parts);
    }

    private static IReadOnlyDictionary<string, RecordPartPayload> ItemPriceUpdateLines(params ItemPriceSeedLine[] rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["price_type_id"] = JsonSerializer.SerializeToElement(row.PriceTypeId),
                ["currency"] = JsonSerializer.SerializeToElement(row.Currency),
                ["unit_price"] = JsonSerializer.SerializeToElement(row.UnitPrice)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> PurchaseReceiptLines(
        params PurchaseReceiptSeedLine[] rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> SalesInvoiceLines(params SalesInvoiceSeedLine[] rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_price"] = JsonSerializer.SerializeToElement(row.UnitPrice),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> InventoryTransferLines(
        params InventoryTransferSeedLine[] rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> InventoryAdjustmentLines(
        params InventoryAdjustmentSeedLine[] rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity_delta"] = JsonSerializer.SerializeToElement(row.QuantityDelta),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> CustomerReturnLines(
        params CustomerReturnSeedLine[] rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_price"] = JsonSerializer.SerializeToElement(row.UnitPrice),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> VendorReturnLines(params VendorReturnSeedLine[] rows)
        => BuildRows(
            rows,
            row => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["item_id"] = JsonSerializer.SerializeToElement(row.ItemId),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_cost"] = JsonSerializer.SerializeToElement(row.UnitCost),
                ["line_amount"] = JsonSerializer.SerializeToElement(row.LineAmount)
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> BuildRows<T>(
        IReadOnlyList<T> rows,
        Func<T, IReadOnlyDictionary<string, JsonElement>> projector)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Count);
        foreach (var row in rows)
        {
            list.Add(projector(row));
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = new(list)
        };
    }

    private static readonly string[] DemoDocumentTypes =
    [
        TradeCodes.ItemPriceUpdate,
        TradeCodes.PurchaseReceipt,
        TradeCodes.SalesInvoice,
        TradeCodes.CustomerPayment,
        TradeCodes.VendorPayment,
        TradeCodes.InventoryTransfer,
        TradeCodes.InventoryAdjustment,
        TradeCodes.CustomerReturn,
        TradeCodes.VendorReturn
    ];

    private readonly record struct ItemPriceSeedLine(
        int Ordinal,
        Guid ItemId,
        Guid PriceTypeId,
        string Currency,
        decimal UnitPrice);

    private readonly record struct PurchaseReceiptSeedLine(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitCost,
        decimal LineAmount);

    private readonly record struct SalesInvoiceSeedLine(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitPrice,
        decimal UnitCost,
        decimal LineAmount);

    private readonly record struct InventoryTransferSeedLine(int Ordinal, Guid ItemId, decimal Quantity);

    private readonly record struct InventoryAdjustmentSeedLine(
        int Ordinal,
        Guid ItemId,
        decimal QuantityDelta,
        decimal UnitCost,
        decimal LineAmount);

    private readonly record struct CustomerReturnSeedLine(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitPrice,
        decimal UnitCost,
        decimal LineAmount);

    private readonly record struct VendorReturnSeedLine(
        int Ordinal,
        Guid ItemId,
        decimal Quantity,
        decimal UnitCost,
        decimal LineAmount);
}
