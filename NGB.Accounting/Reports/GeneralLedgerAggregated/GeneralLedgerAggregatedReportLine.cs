using NGB.Core.Dimensions;

namespace NGB.Accounting.Reports.GeneralLedgerAggregated;

public sealed class GeneralLedgerAggregatedReportLine
{
    public DateTime PeriodUtc { get; init; }
    public Guid DocumentId { get; init; }

    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;

    public Guid CounterAccountId { get; init; }
    public string CounterAccountCode { get; init; } = string.Empty;

    /// <summary>
    /// Dimension set for the selected <see cref="AccountId"/> side (NOT counter-account).
    /// </summary>
    public Guid DimensionSetId { get; init; }

    public DimensionBag Dimensions { get; init; } = DimensionBag.Empty;

    /// <summary>
    /// Enriched display values (DimensionId -&gt; display string) for <see cref="Dimensions"/>.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> DimensionValueDisplays { get; init; } =
        new Dictionary<Guid, string>();

    public decimal DebitAmount { get; init; }
    public decimal CreditAmount { get; init; }

    public decimal RunningBalance { get; init; }
}
