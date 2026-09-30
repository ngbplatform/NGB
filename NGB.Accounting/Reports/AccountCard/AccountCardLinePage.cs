namespace NGB.Accounting.Reports.AccountCard;

public sealed class AccountCardLinePage
{
    public IReadOnlyList<AccountCardLine> Lines { get; init; } = [];

    /// <summary>Net effective movement up to the cursor, calculated with the requested totals.</summary>
    public decimal PrefixDelta { get; init; }

    public bool HasMore { get; init; }

    public AccountCardLineCursor? NextCursor { get; init; }

    /// <summary>
    /// Optional grand totals for the whole filtered range.
    /// Populated by the effective Account Card reader when requested via <see cref="AccountCardLinePageRequest.IncludeTotals"/>.
    /// </summary>
    public decimal? TotalDebit { get; init; }

    /// <summary>
    /// Optional grand totals for the whole filtered range.
    /// Populated by the effective Account Card reader when requested via <see cref="AccountCardLinePageRequest.IncludeTotals"/>.
    /// </summary>
    public decimal? TotalCredit { get; init; }
}
