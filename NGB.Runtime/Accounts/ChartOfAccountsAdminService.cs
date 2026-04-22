using NGB.Accounting.Accounts;
using NGB.Persistence.Accounts;

namespace NGB.Runtime.Accounts;

public sealed class ChartOfAccountsAdminService(IChartOfAccountsRepository repo) : IChartOfAccountsAdminService
{
    public Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetAsync(bool includeDeleted, CancellationToken ct = default)
        => repo.GetForAdminAsync(includeDeleted, ct);

    public Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetByIdsAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct = default)
        => repo.GetAdminByIdsAsync(accountIds, ct);
}
