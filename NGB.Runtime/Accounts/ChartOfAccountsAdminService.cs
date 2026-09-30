using NGB.Accounting.Accounts;
using NGB.Persistence.Accounts;

namespace NGB.Runtime.Accounts;

public sealed class ChartOfAccountsAdminService(IChartOfAccountsRepository repo) : IChartOfAccountsAdminService
{
    public Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetAsync(bool includeDeleted, CancellationToken ct = default)
        => repo.GetForAdminAsync(includeDeleted, ct);

    public Task<ChartOfAccountsAdminPage> GetPageAsync(
        ChartOfAccountsAdminPageQuery query,
        CancellationToken ct = default)
        => repo.GetAdminPageAsync(query, ct);

    public Task<ChartOfAccountsAdminItem?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
        => repo.GetAdminByIdAsync(accountId, ct);

    public Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetByIdsAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct = default)
        => repo.GetAdminByIdsAsync(accountIds, ct);
}
