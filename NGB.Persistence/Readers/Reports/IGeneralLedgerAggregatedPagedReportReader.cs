using NGB.Accounting.Reports.GeneralLedgerAggregated;

namespace NGB.Persistence.Readers.Reports;

public interface IGeneralLedgerAggregatedPagedReportReader
{
    Task<GeneralLedgerAggregatedReportPage> GetPageAsync(
        GeneralLedgerAggregatedReportPageRequest request,
        CancellationToken ct = default);
}
