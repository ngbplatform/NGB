using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.Runtime.Documents.GeneralJournalEntry;

public sealed class PermissionAwareGeneralJournalEntryReadService(
    IGeneralJournalEntryUiService inner,
    INgbAccessChecker access)
    : IAuthorizedGeneralJournalEntryReadService
{
    public async Task<GeneralJournalEntryDetailsDto> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await RequireAsync(ct);

        return await inner.GetByIdAsync(id, ct);
    }

    public async Task<GeneralJournalEntryPageDto> GetPageAsync(
        int offset,
        int limit,
        string? search,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? trash,
        string? cursor,
        CancellationToken ct)
    {
        await RequireAsync(ct);

        return string.IsNullOrWhiteSpace(cursor)
            ? await inner.GetPageAsync(offset, limit, search, dateFrom, dateTo, trash, ct)
            : await inner.GetCursorPageAsync(cursor, limit, search, dateFrom, dateTo, trash, ct);
    }

    private Task RequireAsync(CancellationToken ct)
        => access.RequireAsync(
            NgbResourceKinds.Document,
            AccountingDocumentTypeCodes.GeneralJournalEntry,
            NgbPermissionActions.View,
            ct);
}
