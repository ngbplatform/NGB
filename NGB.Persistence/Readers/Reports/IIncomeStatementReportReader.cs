using NGB.Accounting.Reports.IncomeStatement;

namespace NGB.Persistence.Readers.Reports;

public interface IIncomeStatementReportReader
{
    Task<IncomeStatementReport> GetAsync(IncomeStatementReportRequest request, CancellationToken ct = default);
}
