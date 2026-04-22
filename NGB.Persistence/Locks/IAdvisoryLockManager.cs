using NGB.Core.Locks;

namespace NGB.Persistence.Locks;

/// <summary>
/// Application-level concurrency guards implemented with database advisory locks.
///
/// Why this exists:
/// - In production you must prevent concurrent operations that would corrupt accounting state
///   (e.g. closing a period while posting to the same period).
/// - Advisory locks are lightweight, transactional, and do not require schema changes.
///
/// NOTE:
/// - Lock methods are expected to be used INSIDE an active DB transaction.
/// - Implementations should use provider-specific transaction-scoped locks where possible.
/// </summary>
public interface IAdvisoryLockManager
{
    /// <summary>
    /// Locks a monthly accounting period (the date may be any day within the month; implementations must normalize to YYYY-MM-01).
    /// Use this in PeriodClosingService to prevent concurrent Close/Posting race conditions.
    /// </summary>
    Task LockPeriodAsync(DateOnly period, CancellationToken ct = default);

    /// <summary>
    /// Locks a monthly period within a specific subsystem scope.
    ///
    /// Default implementation falls back to <see cref="LockPeriodAsync(DateOnly, CancellationToken)"/>
    /// for backwards compatibility.
    /// </summary>
    Task LockPeriodAsync(DateOnly period, AdvisoryLockPeriodScope scope, CancellationToken ct = default)
        => LockPeriodAsync(period, ct);

    /// <summary>
    /// Locks a document id. Use this in Posting/Unposting/Reposting to prevent concurrent operations.
    /// </summary>
    Task LockDocumentAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Locks a catalog id. Use this when mutating catalog registry and per-type tables.
    /// </summary>
    Task LockCatalogAsync(Guid catalogId, CancellationToken ct = default);

    /// <summary>
    /// Locks an operational register id.
    ///
    /// Use this to serialize operations whose correctness depends on a register-wide projection chain
    /// (for example, cumulative monthly balances built from prior finalized snapshots).
    /// </summary>
    Task LockOperationalRegisterAsync(Guid registerId, CancellationToken ct = default);
}
