using NGB.Core.Dimensions;

namespace NGB.Accounting.Reports.AccountCard;

/// <summary>
/// A single line of an account card report with running balance.
/// </summary>
public sealed class AccountCardReportLine
{
    public long EntryId { get; init; }
    public DateTime PeriodUtc { get; init; }
    public Guid DocumentId { get; init; }

    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = null!;

    public Guid CounterAccountId { get; init; }
    public string CounterAccountCode { get; init; } = null!;
    
    /// <summary>
    /// Dimension set for the selected <see cref="AccountId"/> side (NOT counter-account).
    /// </summary>
    public Guid DimensionSetId { get; init; }

    public DimensionBag Dimensions { get; init; } = DimensionBag.Empty;

    public IReadOnlyDictionary<Guid, string> DimensionValueDisplays { get; init; } = new Dictionary<Guid, string>();

    public decimal DebitAmount { get; init; }
    public decimal CreditAmount { get; init; }
    public decimal Delta { get; init; }
    public decimal RunningBalance { get; init; }
}
