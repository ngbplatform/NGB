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
    /// <summary>
    /// Returns an exact UTC-date range page ordered by occurrence and MovementId.
    /// Filtering, counting, sorting and paging are performed in the database.
    /// A null limit disables paging while retaining the total count.
    /// </summary>
    Task<OperationalRegisterMovementQueryPage> GetByOccurredAtPageAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions = null,
        int offset = 0,
        int? limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Aggregates a resource by one Dimension value entirely in the database, using storno
    /// semantics and AND filters for the remaining dimensions.
    /// </summary>
    Task<IReadOnlyList<OperationalRegisterDimensionResourceNetRow>> GetResourceNetsByDimensionAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions,
        Guid groupDimensionId,
        string resourceColumnCode,
        CancellationToken ct = default);

    /// <summary>
    /// Database-paged counterpart of <see cref="GetResourceNetsByDimensionAsync"/>.
    /// Positive nets are ordered before negative nets; totals cover the complete filtered result.
    /// </summary>
    Task<OperationalRegisterDimensionResourceNetPage> GetResourceNetsByDimensionPageAsync(
        Guid registerId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyList<DimensionValue>? dimensions,
        Guid groupDimensionId,
        string resourceColumnCode,
        int offset,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the cumulative resource balance as of the end of <paramref name="asOfMonthInclusive"/>.
    /// Implementations may use the latest finalized monthly balance snapshot and only roll forward
    /// movements posted after that snapshot.
    /// </summary>
    Task<IReadOnlyList<OperationalRegisterDimensionResourceNetRow>> GetResourceBalancesByDimensionAsync(
        Guid registerId,
        DateOnly asOfMonthInclusive,
        IReadOnlyList<DimensionValue>? dimensions,
        Guid groupDimensionId,
        string resourceColumnCode,
        CancellationToken ct = default);

    /// <summary>
    /// Database-paged counterpart of <see cref="GetResourceBalancesByDimensionAsync"/>.
    /// </summary>
    Task<OperationalRegisterDimensionResourceNetPage> GetResourceBalancesByDimensionPageAsync(
        Guid registerId,
        DateOnly asOfMonthInclusive,
        IReadOnlyList<DimensionValue>? dimensions,
        Guid groupDimensionId,
        string resourceColumnCode,
        int offset,
        int limit,
        CancellationToken ct = default);

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
