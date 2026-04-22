using NGB.Contracts.Accounting;

namespace NGB.Application.Abstractions.Services;

public interface IGeneralJournalEntryUiService
{
    Task<GeneralJournalEntryPageDto> GetPageAsync(
        int offset,
        int limit,
        string? search,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? trash,
        CancellationToken ct);

    Task<GeneralJournalEntryDetailsDto> GetByIdAsync(Guid id, CancellationToken ct);

    Task<GeneralJournalEntryDetailsDto> CreateDraftAsync(
        CreateGeneralJournalEntryDraftRequestDto request,
        CancellationToken ct);

    Task<GeneralJournalEntryDetailsDto> UpdateHeaderAsync(
        Guid id,
        UpdateGeneralJournalEntryHeaderRequestDto request,
        CancellationToken ct);

    Task<GeneralJournalEntryDetailsDto> ReplaceLinesAsync(
        Guid id,
        ReplaceGeneralJournalEntryLinesRequestDto request,
        CancellationToken ct);

    Task<GeneralJournalEntryDetailsDto> SubmitAsync(Guid id, CancellationToken ct);

    Task<GeneralJournalEntryDetailsDto> ApproveAsync(Guid id, CancellationToken ct);

    Task<GeneralJournalEntryDetailsDto> RejectAsync(
        Guid id,
        GeneralJournalEntryRejectRequestDto request,
        CancellationToken ct);

    Task<GeneralJournalEntryDetailsDto> PostApprovedAsync(Guid id, CancellationToken ct);

    Task<GeneralJournalEntryDetailsDto> ReversePostedAsync(
        Guid id,
        GeneralJournalEntryReverseRequestDto request,
        CancellationToken ct);

    Task<GeneralJournalEntryDetailsDto> MarkForDeletionAsync(Guid id, CancellationToken ct);

    Task<GeneralJournalEntryDetailsDto> UnmarkForDeletionAsync(Guid id, CancellationToken ct);

    Task<GeneralJournalEntryAccountContextDto> GetAccountContextAsync(Guid accountId, CancellationToken ct);
}
