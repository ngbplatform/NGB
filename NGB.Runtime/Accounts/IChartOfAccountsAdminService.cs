using NGB.Accounting.Accounts;

namespace NGB.Runtime.Accounts;

/// <summary>
/// Runtime-facing service for admin UI to browse the Chart of Accounts.
///
/// NOTE:
/// - Posting/closing should use IChartOfAccountsProvider snapshots (active only).
/// - Admin screens may need inactive and/or deleted accounts.
/// </summary>
public interface IChartOfAccountsAdminService
{
    Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetAsync(bool includeDeleted = false, CancellationToken ct = default);

    Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetByIdsAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct = default);
}
