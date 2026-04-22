using NGB.Core.Dimensions;

namespace NGB.Accounting.Reports.TrialBalance;

/// <summary>
/// Trial Balance row for account + dimension set for a period range.
/// Balances are signed numbers: Closing = Opening + (Debit - Credit).
/// </summary>
public sealed class TrialBalanceRow
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = null!;

    public Guid DimensionSetId { get; init; }
    public DimensionBag Dimensions { get; init; } = DimensionBag.Empty;

    /// <summary>
    /// Optional enrichment for UI/report rendering: DimensionId -> display string for the selected ValueId.
    /// (ValueId itself is available via <see cref="Dimensions"/>.)
    /// </summary>
    public IReadOnlyDictionary<Guid, string> DimensionValueDisplays { get; init; }
        = new Dictionary<Guid, string>();

    public decimal OpeningBalance { get; init; }
    public decimal DebitAmount { get; init; }
    public decimal CreditAmount { get; init; }
    public decimal ClosingBalance { get; init; }
}
