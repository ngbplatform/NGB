using NGB.Application.Abstractions.Services;
using NGB.Core.Dimensions;
using NGB.Persistence.OperationalRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Trade.Runtime.Policy;

namespace NGB.Trade.Runtime.Documents.Validation;

public sealed class TradeInventoryAvailabilityService(
    ITradeAccountingPolicyReader policyReader,
    IOperationalRegisterResourceNetReader resourceNetReader,
    ICatalogService catalogs)
{
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
    private static readonly Guid WarehouseDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}");

    internal async Task EnsureSufficientOnHandAsync(
        DateOnly asOf,
        IReadOnlyList<TradeInventoryWithdrawalRequest> withdrawals,
        CancellationToken ct)
    {
        if (withdrawals.Count == 0)
            return;

        var aggregated = withdrawals
            .Where(static x => x.Quantity > 0m)
            .GroupBy(static x => new TradeInventoryBalanceKey(x.WarehouseId, x.ItemId))
            .Select(static group => new TradeInventoryWithdrawalAggregate(
                group.Key,
                group.Sum(static x => x.Quantity)))
            .ToArray();

        if (aggregated.Length == 0)
            return;

        var policy = await policyReader.GetRequiredAsync(ct);
        var balanceValues = await resourceNetReader.GetNetsByDimensionsAsync(
            policy.InventoryMovementsRegisterId,
            aggregated
                .Select(static request => (IReadOnlyList<DimensionValue>)
                [
                    new DimensionValue(WarehouseDimensionId, request.Key.WarehouseId),
                    new DimensionValue(ItemDimensionId, request.Key.ItemId)
                ])
                .ToArray(),
            "qty_delta",
            asOf,
            ct);

        var shortageDetails = new List<(TradeInventoryWithdrawalAggregate Request, decimal Available)>();

        for (var index = 0; index < aggregated.Length; index++)
        {
            var request = aggregated[index];
            var available = balanceValues[index];
            if (available >= request.Quantity)
                continue;

            shortageDetails.Add((request, available));
        }

        if (shortageDetails.Count == 0)
            return;

        var itemDisplayById = await ReadCatalogDisplaysAsync(
            TradeCodes.Item,
            shortageDetails.Select(static x => x.Request.Key.ItemId).Distinct().ToArray(),
            ct);
        var warehouseDisplayById = await ReadCatalogDisplaysAsync(
            TradeCodes.Warehouse,
            shortageDetails.Select(static x => x.Request.Key.WarehouseId).Distinct().ToArray(),
            ct);

        var shortages = shortageDetails
            .OrderBy(x => ResolveDisplay(x.Request.Key.WarehouseId, warehouseDisplayById), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => ResolveDisplay(x.Request.Key.ItemId, itemDisplayById), StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var warehouseDisplay = ResolveDisplay(x.Request.Key.WarehouseId, warehouseDisplayById);
                var itemDisplay = ResolveDisplay(x.Request.Key.ItemId, itemDisplayById);
                return $"{warehouseDisplay} / {itemDisplay}: requested {x.Request.Quantity:0.####}, available {x.Available:0.####}.";
            })
            .ToArray();

        throw new NgbArgumentInvalidException(
            "lines",
            $"Insufficient inventory on hand as of {asOf:yyyy-MM-dd}.{Environment.NewLine}{string.Join(Environment.NewLine, shortages)}");
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ReadCatalogDisplaysAsync(
        string catalogType,
        IReadOnlyList<Guid> ids,
        CancellationToken ct)
    {
        var items = await catalogs.GetByIdsAsync(catalogType, ids, ct);
        return items.ToDictionary(static x => x.Id, static x => x.Label);
    }

    private static string ResolveDisplay(Guid id, IReadOnlyDictionary<Guid, string> displayById)
        => displayById.TryGetValue(id, out var display) ? display : id.ToString("D");
}

internal readonly record struct TradeInventoryWithdrawalRequest(Guid WarehouseId, Guid ItemId, decimal Quantity);

internal readonly record struct TradeInventoryBalanceKey(Guid WarehouseId, Guid ItemId);

internal readonly record struct TradeInventoryWithdrawalAggregate(TradeInventoryBalanceKey Key, decimal Quantity);
