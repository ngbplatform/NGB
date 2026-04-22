using NGB.Core.Dimensions;

namespace NGB.Accounting.Reports.AccountCard;

public sealed class AccountCardLine
{
    public long EntryId { get; init; }
    public DateTime PeriodUtc { get; init; }
    public Guid DocumentId { get; init; }

    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = null!;

    public Guid CounterAccountId { get; init; }
    public string CounterAccountCode { get; init; } = null!;

    /// <summary>
    /// Dimension set for the counter-account side.
    /// </summary>
    public Guid CounterAccountDimensionSetId { get; init; }
    /// <summary>
    /// Dimension set for the selected <see cref="AccountId"/> side (NOT counter-account).
    /// </summary>
    public Guid DimensionSetId { get; init; }

    /// <summary>
    /// Full dimensions resolved for <see cref="DimensionSetId"/>.
    /// </summary>
    public DimensionBag Dimensions { get; set; } = DimensionBag.Empty;

    /// <summary>
    /// Full dimensions resolved for <see cref="CounterAccountDimensionSetId"/>.
    /// </summary>
    public DimensionBag CounterAccountDimensions { get; set; } = DimensionBag.Empty;

    /// <summary>
    /// Enriched display values (DimensionId -&gt; display string) for <see cref="Dimensions"/>.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> DimensionValueDisplays { get; set; } = new Dictionary<Guid, string>();

    /// <summary>
    /// Enriched display values (DimensionId -&gt; display string) for <see cref="CounterAccountDimensions"/>.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> CounterAccountDimensionValueDisplays { get; set; } = new Dictionary<Guid, string>();

    public decimal DebitAmount { get; init; }
    public decimal CreditAmount { get; init; }

    /// <summary>
    /// Signed delta for this line: Debit - Credit (relative to Account).
    /// </summary>
    public decimal Delta => DebitAmount - CreditAmount;
}
