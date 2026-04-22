using NGB.Accounting.Reports.GeneralLedgerAggregated;

namespace NGB.Persistence.Readers.Reports;

public interface IGeneralLedgerAggregatedPageReader
{
    Task<GeneralLedgerAggregatedPage> GetPageAsync(
        GeneralLedgerAggregatedPageRequest request,
        CancellationToken ct = default);
}
