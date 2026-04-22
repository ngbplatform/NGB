using System.Globalization;
using NGB.Contracts.Common;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Trade.Contracts;
using NGB.Trade.Documents;
using NGB.Trade.Pricing;

namespace NGB.Trade.Runtime.Pricing;

public sealed class TradeDocumentLineDefaultsService(
    ITradePricingLookupReader pricingLookupReader,
    ITradeDocumentReaders documentReaders,
    IUnitOfWork uow,
    TimeProvider timeProvider)
{
    public async Task<TradeDocumentLineDefaultsResponseDto> ResolveAsync(
        TradeDocumentLineDefaultsRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var documentType = NormalizeDocumentType(request.DocumentType);
        var rows = request.Rows
            .Where(static row => row.ItemId != Guid.Empty && !string.IsNullOrWhiteSpace(row.RowKey))
            .DistinctBy(static row => row.RowKey, StringComparer.Ordinal)
            .ToArray();

        if (rows.Length == 0)
            return new TradeDocumentLineDefaultsResponseDto([]);

        var asOfDate = ResolveAsOfDate(request.AsOfDate);
        var itemIds = rows.Select(static row => row.ItemId).Distinct().ToArray();
        var itemProfiles = await pricingLookupReader.GetItemSalesProfilesAsync(itemIds, ct);

        var priceTypeByRowKey = ResolvePriceTypes(documentType, request, rows, itemProfiles);
        var requestedPriceKeys = ResolvePriceKeys(documentType, rows, priceTypeByRowKey);
        var requestedCostKeys = ResolveCostKeys(documentType, request, rows);

        var currentPrices = requestedPriceKeys.Count == 0
            ? new Dictionary<TradePriceLookupKey, TradeItemPriceSnapshot>()
            : new Dictionary<TradePriceLookupKey, TradeItemPriceSnapshot>(
                await pricingLookupReader.GetLatestItemPricesAsync(requestedPriceKeys, asOfDate, ct));

        var currentCosts = requestedCostKeys.Count == 0
            ? new Dictionary<TradeWarehouseItemKey, decimal>()
            : new Dictionary<TradeWarehouseItemKey, decimal>(
                await pricingLookupReader.GetLatestUnitCostsAsync(requestedCostKeys, asOfDate, ct));

        var salesInvoiceLinesByItem = documentType == TradeCodes.CustomerReturn && request.SalesInvoiceId is { } salesInvoiceId
            ? await ReadSalesInvoiceLineDefaultsAsync(salesInvoiceId, ct)
            : null;

        var purchaseReceiptLinesByItem = documentType == TradeCodes.VendorReturn && request.PurchaseReceiptId is { } purchaseReceiptId
            ? await ReadPurchaseReceiptLineDefaultsAsync(purchaseReceiptId, ct)
            : null;

        var resultRows = new List<TradeDocumentLineDefaultsRowResultDto>(rows.Length);

        foreach (var row in rows)
        {
            priceTypeByRowKey.TryGetValue(row.RowKey, out var resolvedPriceType);

            decimal? unitPrice = null;
            decimal? unitCost = null;
            string? currency = null;
            RefValueDto? priceType = null;

            if (documentType == TradeCodes.CustomerReturn
                && salesInvoiceLinesByItem is not null
                && salesInvoiceLinesByItem.TryGetValue(row.ItemId, out var salesInvoiceLine))
            {
                unitPrice = salesInvoiceLine.UnitPrice;
                unitCost = salesInvoiceLine.UnitCost;
            }
            else if (documentType == TradeCodes.VendorReturn
                && purchaseReceiptLinesByItem is not null
                && purchaseReceiptLinesByItem.TryGetValue(row.ItemId, out var purchaseReceiptLine))
            {
                unitCost = purchaseReceiptLine.UnitCost;
            }

            if (NeedsUnitPrice(documentType) && unitPrice is null && resolvedPriceType?.PriceTypeId is { } priceTypeId)
            {
                currentPrices.TryGetValue(new TradePriceLookupKey(row.ItemId, priceTypeId), out var currentPrice);
                if (currentPrice is not null)
                {
                    unitPrice = currentPrice.UnitPrice;
                    currency = currentPrice.Currency;
                }
                else if (documentType == TradeCodes.ItemPriceUpdate)
                {
                    currency = TradeCodes.DefaultCurrency;
                }
            }

            if (NeedsUnitCost(documentType) && unitCost is null && request.WarehouseId is { } warehouseId)
            {
                currentCosts.TryGetValue(new TradeWarehouseItemKey(warehouseId, row.ItemId), out var currentCost);
                if (currentCost > 0m)
                    unitCost = currentCost;
            }

            if (documentType == TradeCodes.ItemPriceUpdate && row.PriceTypeId is null && resolvedPriceType is not null)
            {
                priceType = new RefValueDto(
                    resolvedPriceType.PriceTypeId,
                    string.IsNullOrWhiteSpace(resolvedPriceType.PriceTypeDisplay)
                        ? resolvedPriceType.PriceTypeId.ToString("D")
                        : resolvedPriceType.PriceTypeDisplay);

                currency ??= TradeCodes.DefaultCurrency;
            }

            resultRows.Add(new TradeDocumentLineDefaultsRowResultDto(
                RowKey: row.RowKey,
                PriceType: priceType,
                UnitPrice: unitPrice,
                Currency: currency,
                UnitCost: unitCost));
        }

        return new TradeDocumentLineDefaultsResponseDto(resultRows);
    }

    private static string NormalizeDocumentType(string documentType)
    {
        var normalized = documentType?.Trim();
        return normalized switch
        {
            TradeCodes.PurchaseReceipt or
            TradeCodes.SalesInvoice or
            TradeCodes.InventoryAdjustment or
            TradeCodes.CustomerReturn or
            TradeCodes.VendorReturn or
            TradeCodes.ItemPriceUpdate => normalized,
            _ => throw new NgbArgumentInvalidException(nameof(documentType), $"Document type '{documentType}' is not supported for line defaults."),
        };
    }

    private DateOnly ResolveAsOfDate(string? raw)
    {
        if (TryParseDateOnly(raw, out var parsed))
            return parsed;

        return DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
    }

    private static bool TryParseDateOnly(string? raw, out DateOnly value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var normalized = raw.Trim();
        return DateOnly.TryParseExact(normalized, ["yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
               || DateOnly.TryParseExact(normalized, ["MM/dd/yyyy", "M/d/yyyy"], CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out value)
               || DateOnly.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
               || DateOnly.TryParse(normalized, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out value);
    }

    private static Dictionary<string, ResolvedPriceType> ResolvePriceTypes(
        string documentType,
        TradeDocumentLineDefaultsRequestDto request,
        IReadOnlyList<TradeDocumentLineDefaultsRowRequestDto> rows,
        IReadOnlyDictionary<Guid, TradeItemSalesProfile> itemProfiles)
    {
        var result = new Dictionary<string, ResolvedPriceType>(rows.Count, StringComparer.Ordinal);

        foreach (var row in rows)
        {
            Guid? priceTypeId = null;
            string? priceTypeDisplay = null;

            if (documentType == TradeCodes.ItemPriceUpdate)
            {
                priceTypeId = row.PriceTypeId;
            }
            else if (documentType == TradeCodes.SalesInvoice)
            {
                priceTypeId = request.PriceTypeId;
            }

            if (!priceTypeId.HasValue
                && itemProfiles.TryGetValue(row.ItemId, out var profile)
                && profile.DefaultSalesPriceTypeId is { } defaultSalesPriceTypeId)
            {
                priceTypeId = defaultSalesPriceTypeId;
                priceTypeDisplay = profile.DefaultSalesPriceTypeDisplay;
            }

            if (priceTypeId is { } actualPriceTypeId)
            {
                result[row.RowKey] = new ResolvedPriceType(actualPriceTypeId, priceTypeDisplay);
            }
        }

        return result;
    }

    private static HashSet<TradePriceLookupKey> ResolvePriceKeys(
        string documentType,
        IReadOnlyList<TradeDocumentLineDefaultsRowRequestDto> rows,
        IReadOnlyDictionary<string, ResolvedPriceType> priceTypeByRowKey)
    {
        if (!NeedsUnitPrice(documentType))
            return [];

        var keys = new HashSet<TradePriceLookupKey>();
        foreach (var row in rows)
        {
            if (!priceTypeByRowKey.TryGetValue(row.RowKey, out var resolved))
                continue;

            keys.Add(new TradePriceLookupKey(row.ItemId, resolved.PriceTypeId));
        }

        return keys;
    }

    private static HashSet<TradeWarehouseItemKey> ResolveCostKeys(
        string documentType,
        TradeDocumentLineDefaultsRequestDto request,
        IReadOnlyList<TradeDocumentLineDefaultsRowRequestDto> rows)
    {
        if (!NeedsUnitCost(documentType) || request.WarehouseId is not { } warehouseId)
            return [];

        var keys = new HashSet<TradeWarehouseItemKey>();
        foreach (var row in rows)
        {
            keys.Add(new TradeWarehouseItemKey(warehouseId, row.ItemId));
        }

        return keys;
    }

    private async Task<IReadOnlyDictionary<Guid, TradeSalesInvoiceLine>> ReadSalesInvoiceLineDefaultsAsync(
        Guid salesInvoiceId,
        CancellationToken ct)
    {
        var lines = await uow.ExecuteInUowTransactionAsync(
            async innerCt => await documentReaders.ReadSalesInvoiceLinesAsync(salesInvoiceId, innerCt),
            ct);

        return lines
            .GroupBy(static line => line.ItemId)
            .ToDictionary(static group => group.Key, static group => group.OrderBy(static line => line.Ordinal).First());
    }

    private async Task<IReadOnlyDictionary<Guid, TradePurchaseReceiptLine>> ReadPurchaseReceiptLineDefaultsAsync(
        Guid purchaseReceiptId,
        CancellationToken ct)
    {
        var lines = await uow.ExecuteInUowTransactionAsync(
            async innerCt => await documentReaders.ReadPurchaseReceiptLinesAsync(purchaseReceiptId, innerCt),
            ct);

        return lines
            .GroupBy(static line => line.ItemId)
            .ToDictionary(static group => group.Key, static group => group.OrderBy(static line => line.Ordinal).First());
    }

    private static bool NeedsUnitPrice(string documentType)
        => documentType is TradeCodes.SalesInvoice or TradeCodes.CustomerReturn or TradeCodes.ItemPriceUpdate;

    private static bool NeedsUnitCost(string documentType)
        => documentType is TradeCodes.PurchaseReceipt
            or TradeCodes.SalesInvoice
            or TradeCodes.InventoryAdjustment
            or TradeCodes.CustomerReturn
            or TradeCodes.VendorReturn;

    private sealed record ResolvedPriceType(Guid PriceTypeId, string? PriceTypeDisplay);
}
