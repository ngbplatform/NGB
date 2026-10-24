using NGB.Contracts.Accounting;

namespace NGB.Application.Abstractions.Services;

public interface IAuthorizedGeneralJournalEntryReadService
{
    Task<GeneralJournalEntryDetailsDto> GetByIdAsync(Guid id, CancellationToken ct);

    Task<GeneralJournalEntryPageDto> GetPageAsync(
        int offset,
        int limit,
        string? search,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? trash,
        string? cursor,
        CancellationToken ct);
}
