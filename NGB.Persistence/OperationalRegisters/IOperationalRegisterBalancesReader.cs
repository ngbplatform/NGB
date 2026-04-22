using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Read-side boundary for Operational Register balances (per-register opreg_*__balances tables).
///
/// Notes:
/// - Balances are monthly projections derived from movements by a month projector.
/// - Projection tables use replace semantics per month (recompute + replace rows).
/// - Readers should return empty results if the underlying physical table has not been created yet.
/// </summary>
public interface IOperationalRegisterBalancesReader
{
    Task<IReadOnlyList<OperationalRegisterMonthlyProjectionReadRow>> GetByMonthsAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions = null,
        Guid? dimensionSetId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<OperationalRegisterMonthlyProjectionReadRow>> GetPageByMonthsAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions = null,
        Guid? dimensionSetId = null,
        DateOnly? afterPeriodMonth = null,
        Guid? afterDimensionSetId = null,
        int limit = 1000,
        CancellationToken ct = default);
}
