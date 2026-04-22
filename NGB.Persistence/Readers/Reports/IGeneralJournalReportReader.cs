using NGB.Accounting.Reports.GeneralJournal;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// General Journal runtime reader: validates request and returns a cursor-paged journal page.
/// </summary>
public interface IGeneralJournalReportReader
{
    Task<GeneralJournalPage> GetPageAsync(GeneralJournalPageRequest request, CancellationToken ct = default);
}
