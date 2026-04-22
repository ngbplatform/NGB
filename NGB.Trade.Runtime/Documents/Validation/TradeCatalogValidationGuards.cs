using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Services;
using NGB.Tools.Exceptions;

namespace NGB.Trade.Runtime.Documents.Validation;

internal static class TradeCatalogValidationGuards
{
    public static Task EnsureVendorAsync(
        Guid partyId,
        string fieldPath,
        ICatalogService catalogs,
        CancellationToken ct)
        => EnsurePartyRoleAsync(partyId, fieldPath, "is_vendor", "vendor", catalogs, ct);

    public static Task EnsureCustomerAsync(
        Guid partyId,
        string fieldPath,
        ICatalogService catalogs,
        CancellationToken ct)
        => EnsurePartyRoleAsync(partyId, fieldPath, "is_customer", "customer", catalogs, ct);

    public static Task EnsureWarehouseAsync(
        Guid warehouseId,
        string fieldPath,
        ICatalogService catalogs,
        CancellationToken ct)
        => GetRequiredActiveCatalogAsync(TradeCodes.Warehouse, warehouseId, fieldPath, catalogs, ct);

    public static async Task EnsureInventoryItemAsync(
        Guid itemId,
        string fieldPath,
        ICatalogService catalogs,
        CancellationToken ct)
    {
        var item = await GetRequiredActiveCatalogAsync(TradeCodes.Item, itemId, fieldPath, catalogs, ct);
        if (!GetBooleanField(item, "is_inventory_item"))
            throw new NgbArgumentInvalidException(fieldPath, "Selected item must be marked as an inventory item.");
    }

    public static Task EnsurePriceTypeAsync(
        Guid priceTypeId,
        string fieldPath,
        ICatalogService catalogs,
        CancellationToken ct)
        => GetRequiredActiveCatalogAsync(TradeCodes.PriceType, priceTypeId, fieldPath, catalogs, ct);

    public static Task EnsureInventoryAdjustmentReasonAsync(
        Guid reasonId,
        string fieldPath,
        ICatalogService catalogs,
        CancellationToken ct)
        => GetRequiredActiveCatalogAsync(TradeCodes.InventoryAdjustmentReason, reasonId, fieldPath, catalogs, ct);

    private static async Task EnsurePartyRoleAsync(
        Guid partyId,
        string fieldPath,
        string roleField,
        string roleLabel,
        ICatalogService catalogs,
        CancellationToken ct)
    {
        var item = await GetRequiredActiveCatalogAsync(TradeCodes.Party, partyId, fieldPath, catalogs, ct);
        if (!GetBooleanField(item, roleField))
            throw new NgbArgumentInvalidException(fieldPath, $"Selected business partner must be marked as a {roleLabel}.");
    }

    private static async Task<CatalogItemDto> GetRequiredActiveCatalogAsync(
        string catalogType,
        Guid id,
        string fieldPath,
        ICatalogService catalogs,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
            throw new NgbArgumentInvalidException(fieldPath, $"{fieldPath} is required.");

        var item = await catalogs.GetByIdAsync(catalogType, id, ct);
        if (item.IsDeleted || item.IsMarkedForDeletion)
            throw new NgbArgumentInvalidException(fieldPath, $"Referenced {DescribeCatalog(catalogType)} is not available.");

        if (HasField(item, "is_active") && !GetBooleanField(item, "is_active", defaultValue: true))
            throw new NgbArgumentInvalidException(fieldPath, $"Referenced {DescribeCatalog(catalogType)} is inactive.");

        return item;
    }

    private static bool HasField(CatalogItemDto item, string field)
        => item.Payload.Fields is not null && item.Payload.Fields.ContainsKey(field);

    private static bool GetBooleanField(CatalogItemDto item, string field, bool defaultValue = false)
    {
        if (item.Payload.Fields is null || !item.Payload.Fields.TryGetValue(field, out var value))
            return defaultValue;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetInt32(out var parsed) => parsed != 0,
            _ => defaultValue
        };
    }

    private static string DescribeCatalog(string catalogType)
        => catalogType switch
        {
            TradeCodes.Party => "business partner",
            TradeCodes.Item => "item",
            TradeCodes.Warehouse => "warehouse",
            TradeCodes.PriceType => "price type",
            TradeCodes.InventoryAdjustmentReason => "inventory adjustment reason",
            _ => "catalog item"
        };
}
