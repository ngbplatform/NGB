using NGB.Accounting.Registers;

namespace NGB.Persistence.Readers;

public interface IAccountingEntryReader
{
    Task<IReadOnlyList<AccountingEntry>> GetByDocumentAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<AccountingEntry>> GetByDocumentAsync(
        Guid documentId,
        int limit,
        CancellationToken ct = default);
}
