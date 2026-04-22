namespace NGB.Accounting.Reports.GeneralLedgerAggregated;

/// <summary>
/// Stable keyset cursor for aggregated ledger detail rows.
/// </summary>
public sealed class GeneralLedgerAggregatedLineCursor
{
    public DateTime AfterPeriodUtc { get; init; }
    public Guid AfterDocumentId { get; init; }
    public string AfterCounterAccountCode { get; init; } = string.Empty;
    public Guid AfterCounterAccountId { get; init; }
    public Guid AfterDimensionSetId { get; init; }
}
