using NGB.Core.Dimensions;
using NGB.Tools.Extensions;

namespace NGB.Trade.Runtime.Posting;

internal static class TradePostingCommon
{
    private static readonly Guid PartyDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Party}");
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
    private static readonly Guid WarehouseDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}");

    public static DateTime ToOccurredAtUtc(DateOnly date)
        => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    public static DimensionBag PartyBag(Guid partyId)
        => new(
        [
            new DimensionValue(PartyDimensionId, partyId)
        ]);

    public static DimensionBag InventoryBag(Guid itemId, Guid warehouseId)
        => new(
        [
            new DimensionValue(ItemDimensionId, itemId),
            new DimensionValue(WarehouseDimensionId, warehouseId)
        ]);

    public static DimensionBag SalesRevenueBag(Guid partyId, Guid itemId, Guid warehouseId)
        => new(
        [
            new DimensionValue(PartyDimensionId, partyId),
            new DimensionValue(ItemDimensionId, itemId),
            new DimensionValue(WarehouseDimensionId, warehouseId)
        ]);

    public static IReadOnlyDictionary<string, decimal> BuildInventoryMovementResources(decimal quantityDelta)
        => new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["qty_in"] = quantityDelta > 0m ? quantityDelta : 0m,
            ["qty_out"] = quantityDelta < 0m ? -quantityDelta : 0m,
            ["qty_delta"] = quantityDelta
        };

    public static decimal RoundScale4(decimal value)
        => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}
