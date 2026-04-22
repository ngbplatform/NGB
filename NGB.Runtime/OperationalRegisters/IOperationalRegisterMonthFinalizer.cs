namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Legacy module-provided finalizer for an operational register.
///
/// Finalization rebuilds derived projections (turnovers/balances) for a register-month,
/// then marks the month as consistent via <see cref="IOperationalRegisterFinalizationService"/>.
///
/// This interface is kept for backwards compatibility.
/// New modules should implement <see cref="Projections.IOperationalRegisterMonthProjector"/> instead.
///
/// NOTE:
/// - Finalizers are resolved by <see cref="RegisterCodeNorm"/> (lower(trim(code))).
/// - The runner adapts finalizers to projectors.
/// - If no projector/finalizer is registered for a register, the runner will NOT mark the month finalized;
///   it marks the month <c>BlockedNoProjector</c> to avoid repeated retries. After a projector is installed,
///   mark the month Dirty again to re-run finalization.
/// </summary>
public interface IOperationalRegisterMonthFinalizer
{
    /// <summary>
    /// Register code normalized the same way as DB generated column <c>code_norm</c>.
    /// </summary>
    string RegisterCodeNorm { get; }

    /// <summary>
    /// Rebuilds projections for a register-month.
    /// Must run inside the same transaction as finalization marker update.
    /// </summary>
    Task FinalizeMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        DateTime nowUtc,
        CancellationToken ct = default);
}
