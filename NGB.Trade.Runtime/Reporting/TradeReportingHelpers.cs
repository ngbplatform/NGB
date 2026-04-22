using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.Reporting.Canonical;
using NGB.Tools.Extensions;

namespace NGB.Trade.Runtime.Reporting;

internal static class TradeReportingHelpers
{
    private static readonly DateOnly EarliestPeriod = new(2000, 1, 1);

    public static IReadOnlyList<DimensionValue> BuildItemWarehouseFilters(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request)
    {
        var filters = new List<DimensionValue>(capacity: 2);
        var itemId = CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "item_id");
        var warehouseId = CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "warehouse_id");

        if (itemId is { } actualItemId && actualItemId != Guid.Empty)
        {
            filters.Add(new DimensionValue(
                DeterministicGuid.Create($"Dimension|{TradeCodes.Item}"),
                actualItemId));
        }

        if (warehouseId is { } actualWarehouseId && actualWarehouseId != Guid.Empty)
        {
            filters.Add(new DimensionValue(
                DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}"),
                actualWarehouseId));
        }

        return filters;
    }

    public static string GetDisplay(
        DimensionBag bag,
        IReadOnlyDictionary<Guid, string> displays,
        string dimensionCode)
    {
        var dimensionId = DeterministicGuid.Create($"Dimension|{dimensionCode}");
        var value = bag.Items.FirstOrDefault(x => x.DimensionId == dimensionId);
        if (value.ValueId == Guid.Empty)
            return string.Empty;

        return displays.TryGetValue(dimensionId, out var display)
            ? display
            : value.ValueId.ToString("D");
    }

    public static Guid? TryGetValueId(DimensionBag bag, string dimensionCode)
    {
        var dimensionId = DeterministicGuid.Create($"Dimension|{dimensionCode}");
        var value = bag.Items.FirstOrDefault(x => x.DimensionId == dimensionId);
        return value.ValueId == Guid.Empty ? null : value.ValueId;
    }

    public static (DateOnly FromInclusive, DateOnly ToInclusive) GetDateRangeOrCurrentMonth(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        TimeProvider timeProvider)
    {
        var todayUtc = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var rawTo = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "to_utc") ?? todayUtc;
        var rawFrom = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "from_utc")
            ?? new DateOnly(rawTo.Year, rawTo.Month, 1);

        if (rawTo < rawFrom)
        {
            throw Invalid(
                definition,
                "parameters.to_utc",
                $"{CanonicalReportExecutionHelper.GetParameterLabel(definition, "to_utc")} must be on or after {CanonicalReportExecutionHelper.GetParameterLabel(definition, "from_utc")}.");
        }

        return (rawFrom, rawTo);
    }

    public static async Task<IReadOnlyList<InventoryBalanceSnapshot>> ReadInventoryBalancesAsync(
        IOperationalRegisterReadService readService,
        IOperationalRegisterMovementsQueryReader movementsQueryReader,
        Guid registerId,
        DateOnly asOf,
        IReadOnlyList<DimensionValue>? dimensions,
        CancellationToken ct)
    {
        var currentMonth = CanonicalReportExecutionHelper.NormalizeToPeriodMonth(asOf);
        var previousMonth = currentMonth.AddMonths(-1);
        var accumulators = new Dictionary<Guid, InventoryBalanceSnapshot>();
        var movementScanFrom = currentMonth;

        if (previousMonth >= EarliestPeriod)
        {
            OperationalRegisterMonthlyProjectionPageCursor? cursor = null;

            while (true)
            {
                var page = await readService.GetBalancesPageAsync(
                    new OperationalRegisterMonthlyProjectionPageRequest(
                        RegisterId: registerId,
                        FromInclusive: EarliestPeriod,
                        ToInclusive: previousMonth,
                        Dimensions: dimensions is { Count: > 0 } ? dimensions : null,
                        Cursor: cursor,
                        PageSize: 1000),
                    ct);

                foreach (var line in page.Lines)
                {
                    if (!accumulators.TryGetValue(line.DimensionSetId, out var existing)
                        || line.PeriodMonth >= existing.SourcePeriodMonth)
                    {
                        accumulators[line.DimensionSetId] = new InventoryBalanceSnapshot(
                            line.DimensionSetId,
                            line.Values.GetValueOrDefault("qty_delta"),
                            line.Dimensions,
                            line.DimensionValueDisplays,
                            line.PeriodMonth);
                    }
                }

                if (!page.HasMore || page.NextCursor is null)
                    break;

                cursor = page.NextCursor;
            }

            // Fresh environments may not have monthly projections materialized yet.
            // Fall back to a full movements scan for correctness, but keep the fast
            // projection baseline path whenever any snapshot rows exist.
            if (accumulators.Count == 0)
                movementScanFrom = EarliestPeriod;
        }

        var currentMonthMovements = await ReadAllMovementsAsync(
            movementsQueryReader,
            registerId,
            movementScanFrom,
            currentMonth,
            dimensions is { Count: > 0 } ? dimensions : null,
            ct);

        foreach (var movement in currentMonthMovements)
        {
            var occurredOn = DateOnly.FromDateTime(movement.OccurredAtUtc);
            if (occurredOn > asOf)
                continue;

            var signedDelta = movement.Values.GetValueOrDefault("qty_delta");
            if (movement.IsStorno)
                signedDelta = -signedDelta;

            if (!accumulators.TryGetValue(movement.DimensionSetId, out var existing))
            {
                accumulators[movement.DimensionSetId] = new InventoryBalanceSnapshot(
                    movement.DimensionSetId,
                    signedDelta,
                    movement.Dimensions,
                    movement.DimensionValueDisplays,
                    movement.PeriodMonth);
                continue;
            }

            accumulators[movement.DimensionSetId] = existing with
            {
                Quantity = existing.Quantity + signedDelta,
                Bag = existing.Bag.IsEmpty ? movement.Dimensions : existing.Bag,
                Displays = existing.Displays.Count == 0 ? movement.DimensionValueDisplays : existing.Displays
            };
        }

        return accumulators.Values.ToArray();
    }

    public static async Task<IReadOnlyList<OperationalRegisterMovementQueryReadRow>> ReadAllMovementsAsync(
        IOperationalRegisterMovementsQueryReader reader,
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions,
        CancellationToken ct)
    {
        var rows = new List<OperationalRegisterMovementQueryReadRow>();
        long? afterMovementId = null;

        while (true)
        {
            var page = await reader.GetByMonthsAsync(
                registerId: registerId,
                fromInclusive: fromInclusive,
                toInclusive: toInclusive,
                dimensions: dimensions,
                afterMovementId: afterMovementId,
                limit: 1000,
                ct: ct);

            if (page.Count == 0)
                break;

            rows.AddRange(page);
            afterMovementId = page[^1].MovementId;

            if (page.Count < 1000)
                break;
        }

        return rows;
    }

    private static Exception Invalid(
        ReportDefinitionDto definition,
        string fieldPath,
        string message)
        => new NGB.Core.Reporting.Exceptions.ReportLayoutValidationException(
            message,
            fieldPath,
            errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [fieldPath] = [message]
            },
            context: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["reportCode"] = definition.ReportCode
            });
}

internal sealed record InventoryBalanceSnapshot(
    Guid DimensionSetId,
    decimal Quantity,
    DimensionBag Bag,
    IReadOnlyDictionary<Guid, string> Displays,
    DateOnly SourcePeriodMonth);
