namespace NGB.Runtime.Documents.Numbering;

/// <summary>
/// Allocates document numbers for newly-created drafts before their registry rows are inserted.
/// Implementations must reserve every group transactionally so rollback cannot leave gaps.
/// </summary>
public interface IDocumentNumberBatchAllocator
{
    Task<IReadOnlyDictionary<Guid, string>> AllocateAsync(
        IReadOnlyList<DocumentNumberAllocationRequest> requests,
        CancellationToken ct = default);
}

public sealed record DocumentNumberAllocationRequest(Guid DocumentId, string TypeCode, DateTime DateUtc);
