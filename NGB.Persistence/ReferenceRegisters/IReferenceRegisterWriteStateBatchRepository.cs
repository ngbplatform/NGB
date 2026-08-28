using NGB.Accounting.PostingState;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Persistence.ReferenceRegisters;

/// <summary>
/// Optional set-based counterpart of <see cref="IReferenceRegisterWriteStateRepository"/>.
/// Implementations keep multi-register document lifecycle transitions O(1) in database round-trips.
/// </summary>
public interface IReferenceRegisterWriteStateBatchRepository
{
    Task<IReadOnlyDictionary<Guid, PostingStateBeginResult>> TryBeginManyAsync(
        IReadOnlyCollection<Guid> registerIds,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        DateTime startedAtUtc,
        CancellationToken ct = default);

    Task MarkCompletedManyAsync(
        IReadOnlyCollection<Guid> registerIds,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        DateTime completedAtUtc,
        CancellationToken ct = default);
}
