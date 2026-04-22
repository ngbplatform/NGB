using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Low-level store for per-register movements tables (<c>opreg_*__movements</c>).
///
/// Semantics:
/// - Movements are append-only.
/// - Unpost/Repost is implemented by appending storno movements (a copy of the document's existing movements with
///   <c>is_storno = true</c>).
/// </summary>
public interface IOperationalRegisterMovementsStore
{
    /// <summary>
    /// Ensures the physical movements table exists for the given register.
    /// Must be called inside an active transaction.
    /// </summary>
    Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default);

    /// <summary>
    /// Appends movements.
    /// Must be called inside an active transaction.
    /// </summary>
    Task AppendAsync(
        Guid registerId,
        IReadOnlyList<OperationalRegisterMovement> movements,
        CancellationToken ct = default);

    /// <summary>
    /// Appends storno movements for all existing non-storno movements of the given document.
    /// Must be called inside an active transaction.
    /// </summary>
    Task AppendStornoByDocumentAsync(Guid registerId, Guid documentId, CancellationToken ct = default);
}
