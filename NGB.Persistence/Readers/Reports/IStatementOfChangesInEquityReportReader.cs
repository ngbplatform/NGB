using NGB.Accounting.Reports.StatementOfChangesInEquity;

namespace NGB.Persistence.Readers.Reports;

public interface IStatementOfChangesInEquityReportReader
{
    Task<StatementOfChangesInEquityReport> GetAsync(
        StatementOfChangesInEquityReportRequest request,
        CancellationToken ct = default);
}
