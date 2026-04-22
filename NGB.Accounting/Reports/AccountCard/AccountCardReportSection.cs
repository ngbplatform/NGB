namespace NGB.Accounting.Reports.AccountCard;

/// <summary>
/// A section of an account card report (e.g. per day / per document) with totals.
/// Running balance in lines is continuous across sections.
/// </summary>
public sealed class AccountCardReportSection
{
    /// <summary>Human-friendly section title (e.g. "2026-01-02" or "Document 7c2...").</summary>
    public string Title { get; init; } = null!;

    public DateTime FirstPeriodUtc { get; init; }
    public DateTime LastPeriodUtc { get; init; }

    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }

    /// <summary>Signed section delta: TotalDebit - TotalCredit.</summary>
    public decimal Delta { get; init; }

    /// <summary>Running balance right after the last line of the section.</summary>
    public decimal ClosingBalance { get; init; }

    public IReadOnlyList<AccountCardReportLine> Lines { get; init; } = [];
}
