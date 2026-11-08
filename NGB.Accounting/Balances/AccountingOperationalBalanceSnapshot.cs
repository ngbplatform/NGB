namespace NGB.Accounting.Balances;

/// <summary>
/// A lightweight snapshot used by PostingEngine to enforce NegativeBalancePolicy.
/// </summary>
public sealed class AccountingOperationalBalanceSnapshot
{
    public DateOnly Period { get; init; }

    public Guid AccountId { get; init; }

    public Guid DimensionSetId { get; init; }

    public decimal PreviousClosingBalance { get; init; }

    /// <summary>
    /// Signed debit-minus-credit change after the last closed period and before Period.
    /// Defaults to zero for readers that only supply the original snapshot fields.
    /// </summary>
    public decimal PriorOpenPeriodDelta { get; init; }

    public decimal DebitTurnover { get; init; }

    public decimal CreditTurnover { get; init; }
}
