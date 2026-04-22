using NGB.Core.Documents.GeneralJournalEntry;

namespace NGB.Persistence.Documents.GeneralJournalEntry;

public interface IGeneralJournalEntryRepository
{
    Task<GeneralJournalEntryHeaderRecord?> GetHeaderAsync(Guid documentId, CancellationToken ct = default);

    Task<GeneralJournalEntryHeaderRecord?> GetHeaderForUpdateAsync(Guid documentId, CancellationToken ct = default);

    Task UpsertHeaderAsync(GeneralJournalEntryHeaderRecord header, CancellationToken ct = default);

    Task TouchUpdatedAtAsync(Guid documentId, DateTime updatedAtUtc, CancellationToken ct = default);

    Task<IReadOnlyList<GeneralJournalEntryLineRecord>> GetLinesAsync(Guid documentId, CancellationToken ct = default);

    Task ReplaceLinesAsync(
        Guid documentId,
        IReadOnlyList<GeneralJournalEntryLineRecord> lines,
        CancellationToken ct = default);

    Task<IReadOnlyList<GeneralJournalEntryAllocationRecord>> GetAllocationsAsync(
        Guid documentId,
        CancellationToken ct = default);

    Task ReplaceAllocationsAsync(
        Guid documentId,
        IReadOnlyList<GeneralJournalEntryAllocationRecord> allocations,
        CancellationToken ct = default);

    Task<Guid?> TryGetSystemReversalByOriginalAsync(Guid originalDocumentId, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetDueSystemReversalsAsync(DateOnly utcDate, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<GeneralJournalEntryDueSystemReversalCandidate>> GetDueSystemReversalCandidatesAsync(
        DateOnly utcDate,
        int limit,
        DateTime? afterDateUtc = null,
        Guid? afterDocumentId = null,
        CancellationToken ct = default);
}
