namespace NGB.Accounting.Accounts;

/// <summary>
/// Admin-facing projection of a Chart of Accounts item.
///
/// IMPORTANT:
/// - Runtime/posting snapshots should use <see cref="IChartOfAccountsRepository.GetAllAsync"/> which returns only active accounts.
/// - Admin UI may need to show inactive and/or soft-deleted accounts for maintenance and audit.
/// </summary>
public sealed class ChartOfAccountsAdminItem
{
    public Account Account { get; init; } = null!;
    public bool IsActive { get; init; }
    public bool IsDeleted { get; init; }
}
