namespace NGB.Accounting.Reports.AccountCard;

/// <summary>
/// Cursor for keyset pagination over account card lines.
/// </summary>
public sealed class AccountCardLineCursor
{
    public DateTime AfterPeriodUtc { get; init; }
    public long AfterEntryId { get; init; }
}
