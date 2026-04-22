using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;

namespace NGB.Accounting.Balances;

public sealed class NegativeBalanceViolation
{
    public DateOnly Period { get; init; }

    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = null!;
    public string AccountName { get; init; } = null!;
    public AccountType AccountType { get; init; }
    public NegativeBalancePolicy Policy { get; init; }

    public Guid DimensionSetId { get; init; }

    public DimensionBag Dimensions { get; init; } = DimensionBag.Empty;

    public decimal ClosingBalance { get; init; }
}
