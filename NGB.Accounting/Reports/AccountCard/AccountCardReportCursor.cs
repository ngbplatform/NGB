namespace NGB.Accounting.Reports.AccountCard;

/// <summary>
/// Cursor for paged account card reports.
/// Stores both the keyset position and the running balance at that point.
/// </summary>
public sealed class AccountCardReportCursor
{
    public DateTime AfterPeriodUtc { get; init; }
    public long AfterEntryId { get; init; }

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
