using NGB.Accounting.Reports.GeneralJournal;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// General Journal: paged list of register entries for a period range (month-start boundaries).
/// Uses keyset pagination by (PeriodUtc, EntryId).
/// </summary>
public interface IGeneralJournalReader
{
    Task<GeneralJournalPage> GetPageAsync(GeneralJournalPageRequest request, CancellationToken ct = default);
}
