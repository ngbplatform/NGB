namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Specialized read-side boundary for Accounting Consistency verification.
/// Returns flat key-level rows for the requested period and optional previous-period chain check.
/// </summary>
public interface IAccountingConsistencySnapshotReader
{
    Task<AccountingConsistencySnapshot> GetAsync(
        DateOnly period,
        DateOnly? previousPeriodForChainCheck = null,
        CancellationToken ct = default);
}

public sealed record AccountingConsistencySnapshot(IReadOnlyList<AccountingConsistencySnapshotRow> Rows);

public sealed record AccountingConsistencySnapshotRow(
    Guid AccountId,
    string AccountCode,
    Guid DimensionSetId,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal PreviousClosingBalance,
    bool HasCurrentBalanceRow,
    bool HasTurnoverRow,
    bool HasPreviousBalanceRow);
