using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// UI/report oriented read-side boundary for Operational Register movements (per-register opreg_*__movements tables).
///
/// Compared to <see cref="IOperationalRegisterMovementsReader"/>, this query reader supports:
/// - month range queries,
/// - filtering by dimension values (AND semantics),
/// - optional document and storno filters,
/// - optional DimensionSetId filter,
/// - lightweight cursor paging by monotonically increasing MovementId.
/// </summary>
public interface IOperationalRegisterMovementsQueryReader
{
    Task<IReadOnlyList<OperationalRegisterMovementQueryReadRow>> GetByMonthsAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions = null,
        Guid? dimensionSetId = null,
        Guid? documentId = null,
        bool? isStorno = null,
        long? afterMovementId = null,
        int limit = 1000,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the maximum <c>period_month</c> for movements matching the given filters.
    /// Used to extend scan upper bounds when documents may post future-dated movements (e.g., charges with due dates).
    ///
    /// If the physical movements table does not exist yet, returns null.
    /// </summary>
    Task<DateOnly?> GetMaxPeriodMonthAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue>? dimensions = null,
        Guid? dimensionSetId = null,
        Guid? documentId = null,
        bool? isStorno = null,
        CancellationToken ct = default);
}
