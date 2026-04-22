using NGB.Core.Dimensions;

namespace NGB.Persistence.Readers.Reports;

/// <summary>
/// Specialized summary boundary for General Ledger (Aggregated).
/// Returns the account code plus opening/range totals already aggregated for the selected account
/// and dimension scopes, so runtime services do not need generic balance/turnover readers.
/// </summary>
public interface IGeneralLedgerAggregatedSnapshotReader
{
    Task<GeneralLedgerAggregatedSnapshot> GetAsync(
        Guid accountId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes,
        CancellationToken ct = default);
}

public sealed record GeneralLedgerAggregatedSnapshot(
    string AccountCode,
    decimal OpeningBalance,
    decimal TotalDebit,
    decimal TotalCredit)
{
    public decimal ClosingBalance => OpeningBalance + (TotalDebit - TotalCredit);
}
