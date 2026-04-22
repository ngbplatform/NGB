using NGB.Accounting.Accounts;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Specialized read-side boundary for Statement of Changes in Equity.
/// Returns already-aggregated account-level opening and closing balances for Equity and P&L sections,
/// so runtime services do not need generic balance/turnover readers or cross-report composition.
/// </summary>
public interface IStatementOfChangesInEquitySnapshotReader
{
    Task<StatementOfChangesInEquitySnapshot> GetAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default);
}

public sealed record StatementOfChangesInEquitySnapshot(
    IReadOnlyList<StatementOfChangesInEquitySnapshotRow> Rows,
    DateOnly? OpeningLatestClosedPeriod,
    int OpeningRollForwardPeriods,
    DateOnly? ClosingLatestClosedPeriod,
    int ClosingRollForwardPeriods);

public sealed record StatementOfChangesInEquitySnapshotRow(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    StatementSection StatementSection,
    decimal OpeningBalance,
    decimal ClosingBalance);
