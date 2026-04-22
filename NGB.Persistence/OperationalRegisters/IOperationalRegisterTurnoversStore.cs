using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Storage for module-defined monthly turnovers projections.
/// Physical table name is register-specific: <c>opreg_*__turnovers</c>.
/// </summary>
public interface IOperationalRegisterTurnoversStore
{
    /// <summary>
    /// Ensures the physical turnovers table exists for the given register.
    /// Requires an active transaction.
    /// </summary>
    Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default);

    /// <summary>
    /// Replaces turnovers for a specific month (YYYY-MM-01).
    /// Requires an active transaction.
    /// </summary>
    Task ReplaceForMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow> rows,
        CancellationToken ct = default);

    /// <summary>
    /// Reads turnovers for a specific month (YYYY-MM-01).
    /// If the physical table doesn't exist yet, returns an empty list.
    /// </summary>
    Task<IReadOnlyList<OperationalRegisterMonthlyProjectionRow>> GetByMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        Guid? dimensionSetId = null,
        CancellationToken ct = default);
}
