using NGB.OperationalRegisters.Contracts;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// High-level helper for applying document-scoped movements into an Operational Register.
///
/// Semantics:
/// - Movements tables are append-only.
/// - Unpost/Repost is represented by appending storno movements (copy of original movements with <c>is_storno=true</c>).
/// - Post appends new movements.
/// - Repost appends storno for previous movements, then appends the new movements.
///
/// This helper delegates idempotency, locking and dirty tracking to <see cref="IOperationalRegisterWriteEngine"/>.
/// </summary>
public interface IOperationalRegisterMovementsApplier
{
    /// <summary>
    /// Applies movements for <paramref name="documentId"/> to the register.
    ///
    /// Notes:
    /// - For Post: appends <paramref name="movements"/>.
    /// - For Unpost: ignores <paramref name="movements"/> and appends storno movements by <paramref name="documentId"/>.
    /// - For Repost: appends storno by <paramref name="documentId"/>, then appends <paramref name="movements"/>.
    ///
    /// <paramref name="affectedPeriods"/> controls month locks and Dirty markers.
    /// If omitted:
    /// - For Post: derived from <paramref name="movements"/>.
    /// - For Unpost/Repost: union of months derived from existing movements and (for Repost) <paramref name="movements"/>.
    /// </summary>
    Task<OperationalRegisterWriteResult> ApplyMovementsForDocumentAsync(
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation,
        IReadOnlyList<OperationalRegisterMovement> movements,
        IReadOnlyCollection<DateOnly>? affectedPeriods = null,
        bool manageTransaction = true,
        CancellationToken ct = default);
}
