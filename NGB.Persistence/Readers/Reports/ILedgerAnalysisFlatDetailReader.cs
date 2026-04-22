using NGB.Accounting.Reports.LedgerAnalysis;

namespace NGB.Persistence.Readers.Reports;

public interface ILedgerAnalysisFlatDetailReader
{
    Task<LedgerAnalysisFlatDetailPage> GetPageAsync(
        LedgerAnalysisFlatDetailPageRequest request,
        CancellationToken ct = default);
}
