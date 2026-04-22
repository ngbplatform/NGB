using NGB.Accounting.Reports.CashFlowIndirect;

namespace NGB.Persistence.Readers.Reports;

public interface ICashFlowIndirectReportReader
{
    Task<CashFlowIndirectReport> GetAsync(CashFlowIndirectReportRequest request, CancellationToken ct = default);
}
