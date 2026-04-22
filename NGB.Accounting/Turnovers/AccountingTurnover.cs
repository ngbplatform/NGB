using NGB.Core.Dimensions;

namespace NGB.Accounting.Turnovers;

public sealed class AccountingTurnover
{
    public DateOnly Period { get; init; }

    public Guid AccountId { get; init; }

    /// <summary>
    /// The canonical DimensionSetId backing this turnover row.
    /// </summary>
    public Guid DimensionSetId { get; init; }

    /// <summary>
    /// Canonical analytical dimensions for this row.
    ///
    /// Filled by read-model components (readers) by resolving <see cref="DimensionSetId"/>.
    /// </summary>
    public DimensionBag Dimensions { get; init; } = DimensionBag.Empty;

    // Display-friendly (optional). Readers can join accounts to fill.
    public string? AccountCode { get; init; }

    public decimal DebitAmount { get; init; }
    public decimal CreditAmount { get; init; }
}
