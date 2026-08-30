using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;

namespace NGB.Persistence.Documents.GeneralJournalEntry;

public sealed record GeneralJournalEntryListItemRecord(
    Guid Id,
    DateTime DateUtc,
    string? Number,
    string? Display,
    DocumentStatus DocumentStatus,
    bool IsMarkedForDeletion,
    GeneralJournalEntryModels.JournalType JournalType,
    GeneralJournalEntryModels.Source Source,
    GeneralJournalEntryModels.ApprovalState ApprovalState,
    string? ReasonCode,
    string? Memo,
    string? ExternalReference,
    bool AutoReverse,
    DateOnly? AutoReverseOnUtc,
    Guid? ReversalOfDocumentId,
    string? PostedBy,
    DateTime? PostedAtUtc);

public sealed record GeneralJournalEntryPageRecord(
    IReadOnlyList<GeneralJournalEntryListItemRecord> Items,
    int Offset,
    int Limit,
    int Total,
    bool HasMore = false,
    DateTime? NextAfterDateUtc = null,
    DateTime? NextAfterCreatedAtUtc = null,
    Guid? NextAfterId = null);

public sealed record GeneralJournalEntryPageCursor(
    int Offset,
    int Total,
    DateTime? AfterDateUtc = null,
    DateTime? AfterCreatedAtUtc = null,
    Guid? AfterId = null);

public interface IGeneralJournalEntryUiQueryRepository
{
    Task<GeneralJournalEntryPageRecord> GetPageAsync(
        int offset,
        int limit,
        string? search,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? trash,
        CancellationToken ct = default);

    async Task<GeneralJournalEntryPageRecord> GetCursorPageAsync(
        GeneralJournalEntryPageCursor cursor,
        int limit,
        string? search,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? trash,
        CancellationToken ct = default)
    {
        var page = await GetPageAsync(cursor.Offset, limit, search, dateFrom, dateTo, trash, ct);
        return page with { HasMore = cursor.Offset + page.Items.Count < page.Total };
    }
}
