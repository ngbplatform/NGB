namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Prepares default projections up to a committed movement boundary in an owned ReadCommitted
/// transaction. A separate finalizer lock must serialize preparations for this register.
/// The caller must complete catch-up and mark Finalized before committing the same transaction.
/// </summary>
public interface IOperationalRegisterProjectionPreparation
{
    Task<IOperationalRegisterPreparedProjection> PrepareMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        CancellationToken ct = default);
}

/// <summary>A single-use prepared result that cannot outlive its transaction.</summary>
public interface IOperationalRegisterPreparedProjection
{
    /// <summary>
    /// Acquires publication locks and applies movements committed since the boundary.
    /// The locks remain held until commit/rollback. Any failure requires rollback.
    /// </summary>
    Task CompleteAsync(CancellationToken ct = default);
}
