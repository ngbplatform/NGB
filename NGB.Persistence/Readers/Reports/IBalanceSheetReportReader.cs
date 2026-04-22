using NGB.Accounting.Reports.BalanceSheet;

namespace NGB.Persistence.Readers.Reports;

public interface IBalanceSheetReportReader
{
    Task<BalanceSheetReport> GetAsync(BalanceSheetReportRequest request, CancellationToken ct = default);
}
