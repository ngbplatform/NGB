using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.OperationalRegisters.Projections;

/// <summary>
/// Default production projector for operational registers.
///
/// Notes:
/// - Uses SQL aggregation from the ground-truth movements table.
/// - Writes month-local net rows to turnovers.
/// - Builds balances as cumulative end-of-month snapshots by applying current turnovers
///   over the latest finalized prior balance snapshot, if any.
/// - Modules that need different balance semantics can override this with a typed projector.
/// </summary>
public sealed class DefaultOperationalRegisterMonthProjector(
    IOperationalRegisterMonthlyProjectionAggregator aggregator,
    IOperationalRegisterFinalizationRepository finalizations,
    IOperationalRegisterTurnoversStore turnovers,
    IOperationalRegisterBalancesStore balances)
    : IOperationalRegisterDefaultMonthProjector
{
    public async Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
    {
        if (context.RegisterId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(context));

        // Keep the lock order aligned with explicit projectors:
        // schema first, then movements read, then projection replace.
        await turnovers.EnsureSchemaAsync(context.RegisterId, ct);
        await balances.EnsureSchemaAsync(context.RegisterId, ct);

        var turnoverRows = await aggregator.AggregateMonthAsync(context.RegisterId, context.PeriodMonth, ct);
        await turnovers.ReplaceForMonthAsync(context.RegisterId, context.PeriodMonth, turnoverRows, ct);

        var previousFinalizedPeriod = await finalizations.GetLatestFinalizedPeriodBeforeAsync(
            context.RegisterId,
            context.PeriodMonth,
            ct);

        var previousBalanceRows = previousFinalizedPeriod is { } previousPeriod
            ? await balances.GetByMonthAsync(context.RegisterId, previousPeriod, ct: ct)
            : [];

        var balanceRows = BuildCumulativeBalanceRows(previousBalanceRows, turnoverRows);
        await balances.ReplaceForMonthAsync(context.RegisterId, context.PeriodMonth, balanceRows, ct);
    }

    private static IReadOnlyList<OperationalRegisterMonthlyProjectionRow> BuildCumulativeBalanceRows(
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow> previousBalances,
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow> turnovers)
    {
        var merged = new Dictionary<Guid, Dictionary<string, decimal>>();

        foreach (var row in previousBalances)
        {
            if (!merged.TryGetValue(row.DimensionSetId, out var bucket))
            {
                bucket = new Dictionary<string, decimal>(StringComparer.Ordinal);
                merged.Add(row.DimensionSetId, bucket);
            }

            foreach (var (key, value) in row.Values)
            {
                bucket[key] = value;
            }
        }

        foreach (var row in turnovers)
        {
            if (!merged.TryGetValue(row.DimensionSetId, out var bucket))
            {
                bucket = new Dictionary<string, decimal>(StringComparer.Ordinal);
                merged.Add(row.DimensionSetId, bucket);
            }

            foreach (var (key, value) in row.Values)
            {
                bucket.TryGetValue(key, out var current);
                bucket[key] = current + value;
            }
        }

        return merged
            .Where(kv => kv.Value.Values.Any(v => v != 0m))
            .OrderBy(kv => kv.Key)
            .Select(kv => new OperationalRegisterMonthlyProjectionRow(kv.Key, kv.Value))
            .ToArray();
    }
}
