using NGB.Accounting.CashFlow;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Specialized read-side boundary for the indirect cash flow statement.
/// Returns already-aggregated operating / investing / financing lines plus beginning and ending cash.
/// </summary>
public interface ICashFlowIndirectSnapshotReader
{
    Task<CashFlowIndirectSnapshot> GetAsync(DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default);
}

public sealed record CashFlowIndirectSnapshot(
    decimal NetIncome,
    IReadOnlyList<CashFlowIndirectSnapshotLine> OperatingLines,
    IReadOnlyList<CashFlowIndirectSnapshotLine> InvestingLines,
    IReadOnlyList<CashFlowIndirectSnapshotLine> FinancingLines,
    decimal BeginningCash,
    decimal EndingCash,
    DateOnly? BeginningLatestClosedPeriod,
    int BeginningRollForwardPeriods,
    DateOnly? EndingLatestClosedPeriod,
    int EndingRollForwardPeriods,
    IReadOnlyList<CashFlowIndirectUnclassifiedCashRow> UnclassifiedCashRows);

public sealed record CashFlowIndirectSnapshotLine(
    CashFlowSection Section,
    string LineCode,
    string Label,
    int SortOrder,
    decimal Amount);

public sealed record CashFlowIndirectUnclassifiedCashRow(string AccountCode, string AccountName, decimal Amount);
