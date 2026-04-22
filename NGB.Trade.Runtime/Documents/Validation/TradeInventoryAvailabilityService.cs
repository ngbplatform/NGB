using NGB.Application.Abstractions.Services;
using NGB.Core.Dimensions;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Trade.Runtime.Policy;
using NGB.Trade.Runtime.Reporting;

namespace NGB.Trade.Runtime.Documents.Validation;

public sealed class TradeInventoryAvailabilityService(
    ITradeAccountingPolicyReader policyReader,
    IOperationalRegisterReadService readService,
    IOperationalRegisterMovementsQueryReader movementsQueryReader,
    ICatalogService catalogs)
{
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
        var balancesByKey = new Dictionary<TradeInventoryBalanceKey, decimal>();
        var displayByKey = new Dictionary<TradeInventoryBalanceKey, (string Warehouse, string Item)>();

        foreach (var warehouseId in aggregated.Select(static x => x.Key.WarehouseId).Distinct())
        {
            var balances = await TradeReportingHelpers.ReadInventoryBalancesAsync(
                readService,
                movementsQueryReader,
                policy.InventoryMovementsRegisterId,
                asOf,
                [new DimensionValue(WarehouseDimensionId, warehouseId)],
                ct);

            foreach (var balance in balances)
            {
                var itemId = TradeReportingHelpers.TryGetValueId(balance.Bag, TradeCodes.Item);
                var balanceWarehouseId = TradeReportingHelpers.TryGetValueId(balance.Bag, TradeCodes.Warehouse);

                if (itemId is null || balanceWarehouseId is null)
                    continue;

                var key = new TradeInventoryBalanceKey(balanceWarehouseId.Value, itemId.Value);
                balancesByKey[key] = balance.Quantity;
                displayByKey[key] = (
                    TradeReportingHelpers.GetDisplay(balance.Bag, balance.Displays, TradeCodes.Warehouse),
                    TradeReportingHelpers.GetDisplay(balance.Bag, balance.Displays, TradeCodes.Item));
            }
        }

        var shortageDetails = new List<(TradeInventoryWithdrawalAggregate Request, decimal Available)>();

        foreach (var request in aggregated)
        {
            var available = balancesByKey.GetValueOrDefault(request.Key);
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
            .OrderBy(x => ResolveWarehouseDisplay(x.Request.Key, displayByKey, warehouseDisplayById), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => ResolveItemDisplay(x.Request.Key, displayByKey, itemDisplayById), StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var warehouseDisplay = ResolveWarehouseDisplay(x.Request.Key, displayByKey, warehouseDisplayById);
                var itemDisplay = ResolveItemDisplay(x.Request.Key, displayByKey, itemDisplayById);
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
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var items = await catalogs.GetByIdsAsync(catalogType, ids, ct);
        return items.ToDictionary(static x => x.Id, static x => x.Label);
    }

    private static string ResolveWarehouseDisplay(
        TradeInventoryBalanceKey key,
        IReadOnlyDictionary<TradeInventoryBalanceKey, (string Warehouse, string Item)> displayByKey,
        IReadOnlyDictionary<Guid, string> warehouseDisplayById)
        => displayByKey.TryGetValue(key, out var display) && !string.IsNullOrWhiteSpace(display.Warehouse)
            ? display.Warehouse
            : warehouseDisplayById.TryGetValue(key.WarehouseId, out var fallback)
                ? fallback
                : key.WarehouseId.ToString("D");

    private static string ResolveItemDisplay(
        TradeInventoryBalanceKey key,
        IReadOnlyDictionary<TradeInventoryBalanceKey, (string Warehouse, string Item)> displayByKey,
        IReadOnlyDictionary<Guid, string> itemDisplayById)
        => displayByKey.TryGetValue(key, out var display) && !string.IsNullOrWhiteSpace(display.Item)
            ? display.Item
            : itemDisplayById.TryGetValue(key.ItemId, out var fallback)
                ? fallback
                : key.ItemId.ToString("D");
}

internal readonly record struct TradeInventoryWithdrawalRequest(Guid WarehouseId, Guid ItemId, decimal Quantity);

internal readonly record struct TradeInventoryBalanceKey(Guid WarehouseId, Guid ItemId);

internal readonly record struct TradeInventoryWithdrawalAggregate(TradeInventoryBalanceKey Key, decimal Quantity);
