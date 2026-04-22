using NGB.Core.Dimensions;
using NGB.Persistence.OperationalRegisters;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Helpers for UI/read-model queries that scan Operational Register movements by month range.
/// 
/// Some read-models want to include future-dated posted movements (e.g. posted documents with OccurredAtUtc in a future month).
/// In such cases, the scan upper bound should be extended to the maximum month that actually has matching movements.
/// </summary>
public static class OperationalRegisterScanBoundaries
{
    public static async Task<DateOnly> ResolveToMonthInclusiveAsync(
        IOperationalRegisterMovementsQueryReader reader,
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly nowMonth,
        IReadOnlyList<DimensionValue>? dimensions = null,
        Guid? dimensionSetId = null,
        Guid? documentId = null,
        bool? isStorno = null,
        CancellationToken ct = default)
    {
        var max = await reader.GetMaxPeriodMonthAsync(
            registerId,
            dimensions: dimensions,
            dimensionSetId: dimensionSetId,
            documentId: documentId,
            isStorno: isStorno,
            ct: ct);

        var resolved = max is { } m && m > nowMonth ? m : nowMonth;
        return resolved < fromInclusive ? fromInclusive : resolved;
    }
}
