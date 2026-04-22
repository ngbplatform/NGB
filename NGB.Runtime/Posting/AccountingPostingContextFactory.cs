using NGB.Accounting.Accounts;
using NGB.Accounting.Posting;

namespace NGB.Runtime.Posting;

/// <summary>
/// Creates an in-memory accounting posting context.
/// </summary>
public sealed class AccountingPostingContextFactory(IChartOfAccountsProvider chartProvider)
    : IAccountingPostingContextFactory
{
    public async Task<IAccountingPostingContext> CreateAsync(CancellationToken ct = default)
    {
        var chart = await chartProvider.GetAsync(ct);
        return new AccountingPostingContext(chart);
    }
}
