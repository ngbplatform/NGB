using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Persistence boundary for operational register finalization markers.
///
/// Table:
/// - operational_register_finalizations
///
/// Notes:
/// - This is not the accounting closed-period policy.
/// - Finalization is used to mark derived projections (turnovers/balances) as consistent.
/// </summary>
public interface IOperationalRegisterFinalizationRepository
{
    Task<OperationalRegisterFinalization?> GetAsync(
        Guid registerId,
        DateOnly period,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a register-period as finalized.
    /// Requires an active transaction.
    /// </summary>
    Task MarkFinalizedAsync(
        Guid registerId,
        DateOnly period,
        DateTime finalizedAtUtc,
        DateTime nowUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a register-period as dirty.
    /// Requires an active transaction.
    /// </summary>
    Task MarkDirtyAsync(
        Guid registerId,
        DateOnly period,
        DateTime dirtySinceUtc,
        DateTime nowUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a register-period as blocked because no projector is registered for this register.
    /// Requires an active transaction.
    /// </summary>
    Task MarkBlockedNoProjectorAsync(
        Guid registerId,
        DateOnly period,
        DateTime blockedSinceUtc,
        string blockedReason,
        DateTime nowUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Lists dirty register-months ordered for processing.
    /// Does not require an active transaction.
    /// </summary>
    Task<IReadOnlyList<OperationalRegisterFinalization>> GetDirtyAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Lists blocked register-months ordered for diagnostics.
    /// Does not require an active transaction.
    /// </summary>
    Task<IReadOnlyList<OperationalRegisterFinalization>> GetBlockedAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Lists dirty register-months across all registers ordered for processing.
    /// Does not require an active transaction.
    /// </summary>
    Task<IReadOnlyList<OperationalRegisterFinalization>> GetDirtyAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Lists blocked register-months across all registers ordered for diagnostics.
    /// Does not require an active transaction.
    /// </summary>
    Task<IReadOnlyList<OperationalRegisterFinalization>> GetBlockedAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Lists tracked periods for a register starting from the specified month (inclusive).
    /// This includes months in any status and is intended for projection-chain invalidation.
    /// </summary>
    Task<IReadOnlyList<DateOnly>> GetTrackedPeriodsOnOrAfterAsync(
        Guid registerId,
        DateOnly fromInclusive,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the latest finalized period strictly before the specified month, or <c>null</c> if none exists.
    /// </summary>
    Task<DateOnly?> GetLatestFinalizedPeriodBeforeAsync(
        Guid registerId,
        DateOnly beforeExclusive,
        CancellationToken ct = default);
}
