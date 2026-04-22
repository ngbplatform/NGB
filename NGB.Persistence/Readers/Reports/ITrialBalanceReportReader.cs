using NGB.Accounting.Reports.TrialBalance;

namespace NGB.Persistence.Readers.Reports;

public interface ITrialBalanceReportReader
{
    Task<TrialBalanceReportPage> GetPageAsync(TrialBalanceReportPageRequest request, CancellationToken ct = default);
}
