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

    public decimal DebitTurnover { get; init; }

    public decimal CreditTurnover { get; init; }
}
