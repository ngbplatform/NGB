using NGB.Accounting.Reports.AccountingConsistency;

namespace NGB.Persistence.Readers.Reports;

public interface IAccountingConsistencyReportReader
{
    /// <summary>
    /// Runs consistency checks for the specified period (month-start DateOnly).
    /// Intended for production diagnostics and smoke checks.
    /// </summary>
    Task<AccountingConsistencyReport> RunForPeriodAsync(
        DateOnly period,
        DateOnly? previousPeriodForChainCheck = null,
        CancellationToken ct = default);
}
