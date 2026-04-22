using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Specialized read-side boundary for Income Statement activity over a range.
/// Returns already-aggregated debit/credit activity by account and includes the
/// minimum account metadata needed to shape the statement.
/// </summary>
public interface IIncomeStatementSnapshotReader
{
    Task<IncomeStatementSnapshot> GetAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes,
        bool includeZeroLines,
        CancellationToken ct = default);
}

public sealed record IncomeStatementSnapshot(IReadOnlyList<IncomeStatementSnapshotRow> Rows);

public sealed record IncomeStatementSnapshotRow(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    StatementSection StatementSection,
    decimal DebitAmount,
    decimal CreditAmount);
