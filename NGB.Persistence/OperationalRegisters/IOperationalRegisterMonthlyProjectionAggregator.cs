using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Computes a month-local net projection for an operational register directly from movements.
///
/// Semantics:
/// - Aggregation key: <c>(period_month, dimension_set_id)</c>.
/// - Resource values are computed as <c>SUM(non-storno) - SUM(storno)</c>.
/// - Missing physical movements table yields an empty result.
/// </summary>
public interface IOperationalRegisterMonthlyProjectionAggregator
{
    Task<IReadOnlyList<OperationalRegisterMonthlyProjectionRow>> AggregateMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        CancellationToken ct = default);
}
