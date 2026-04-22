namespace NGB.Accounting.Reports.GeneralLedgerAggregated;

/// <summary>
/// Report cursor for paged aggregated ledger.
/// Stores both the keyset position and the running balance at that point.
/// </summary>
public sealed class GeneralLedgerAggregatedReportCursor
{
    public DateTime AfterPeriodUtc { get; init; }
    public Guid AfterDocumentId { get; init; }
    public string AfterCounterAccountCode { get; init; } = string.Empty;
    public Guid AfterCounterAccountId { get; init; }
    public Guid AfterDimensionSetId { get; init; }

    /// <summary>
    /// Running balance right after the last line of the previous page.
    /// Next page starts from this balance.
    /// </summary>
    public decimal RunningBalance { get; init; }

    /// <summary>
    /// Grand total debit for the whole filtered range.
    /// Optional for backward-compatible cursors created before totals were embedded.
    /// </summary>
    public decimal? TotalDebit { get; init; }

    /// <summary>
    /// Grand total credit for the whole filtered range.
    /// Optional for backward-compatible cursors created before totals were embedded.
    /// </summary>
    public decimal? TotalCredit { get; init; }

    /// <summary>
    /// Grand closing balance for the whole filtered range.
    /// Optional for backward-compatible cursors created before totals were embedded.
    /// </summary>
    public decimal? ClosingBalance { get; init; }
}
