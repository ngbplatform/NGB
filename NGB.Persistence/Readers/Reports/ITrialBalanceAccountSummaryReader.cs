using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;

namespace NGB.Persistence.Readers.Reports;

/// <summary>One row per account after dimension filtering. Ordered by type, code, and ID.
/// All rows, including account labels and balances, belong to one database snapshot.</summary>
public interface ITrialBalanceAccountSummaryReader
{
    IAsyncEnumerable<TrialBalanceAccountSummary> ReadAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes,
        CancellationToken ct = default);
}

public sealed record TrialBalanceAccountSummary(
    Guid AccountId,
    string Code,
    string Name,
    AccountType Type,
    decimal OpeningBalance,
    decimal DebitAmount,
    decimal CreditAmount);
