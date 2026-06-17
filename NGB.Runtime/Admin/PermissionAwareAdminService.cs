using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;
using NGB.Contracts.Services;
using NGB.Core.Reporting;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.Runtime.Admin;

public sealed class PermissionAwareAdminService(AdminService inner, INgbAccessChecker access) : IAdminService
{
    public async Task<MainMenuDto> GetMainMenuAsync(CancellationToken ct)
    {
        var menu = await inner.GetMainMenuAsync(ct);
        if (menu.Groups.Count == 0)
            return menu;

        var snapshot = await access.GetSnapshotAsync(ct);
        var groups = new List<MainMenuGroupDto>(menu.Groups.Count);

        foreach (var group in menu.Groups)
        {
            var items = new List<MainMenuItemDto>(group.Items.Count);
            foreach (var item in group.Items)
            {
                if (IsAuthorized(item, snapshot))
                    items.Add(item);
            }

            if (items.Count > 0)
                groups.Add(group with { Items = items });
        }

        return new MainMenuDto(groups);
    }

    public async Task<ChartOfAccountsMetadataDto> GetChartOfAccountsMetadataAsync(CancellationToken ct)
    {
        await RequireCoaViewAsync(ct);
        return await inner.GetChartOfAccountsMetadataAsync(ct);
    }

    public async Task<ChartOfAccountsPageDto> GetChartOfAccountsPageAsync(
        ChartOfAccountsPageRequestDto request,
        CancellationToken ct)
    {
        await RequireCoaViewAsync(ct);
        return await inner.GetChartOfAccountsPageAsync(request, ct);
    }

    public async Task<ChartOfAccountsAccountDto> GetChartOfAccountAsync(Guid accountId, CancellationToken ct)
    {
        await RequireCoaViewAsync(ct);
        return await inner.GetChartOfAccountAsync(accountId, ct);
    }

    public async Task<IReadOnlyList<LookupItemDto>> GetChartOfAccountsByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken ct)
    {
        await RequireCoaViewAsync(ct);
        return await inner.GetChartOfAccountsByIdsAsync(ids, ct);
    }

    public async Task<ChartOfAccountsAccountDto> CreateChartOfAccountAsync(
        ChartOfAccountsUpsertRequestDto request,
        CancellationToken ct)
    {
        await RequireCoaManageAsync(ct);
        return await inner.CreateChartOfAccountAsync(request, ct);
    }

    public async Task<ChartOfAccountsAccountDto> UpdateChartOfAccountAsync(
        Guid accountId,
        ChartOfAccountsUpsertRequestDto request,
        CancellationToken ct)
    {
        await RequireCoaManageAsync(ct);
        return await inner.UpdateChartOfAccountAsync(accountId, request, ct);
    }

    public async Task MarkChartOfAccountForDeletionAsync(Guid accountId, CancellationToken ct)
    {
        await RequireCoaManageAsync(ct);
        await inner.MarkChartOfAccountForDeletionAsync(accountId, ct);
    }

    public async Task UnmarkChartOfAccountForDeletionAsync(Guid accountId, CancellationToken ct)
    {
        await RequireCoaManageAsync(ct);
        await inner.UnmarkChartOfAccountForDeletionAsync(accountId, ct);
    }

    public async Task SetChartOfAccountActiveAsync(Guid accountId, bool isActive, CancellationToken ct)
    {
        await RequireCoaManageAsync(ct);
        await inner.SetChartOfAccountActiveAsync(accountId, isActive, ct);
    }

    private Task RequireCoaViewAsync(CancellationToken ct)
        => access.RequireAsync(NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.View, ct);

    private Task RequireCoaManageAsync(CancellationToken ct)
        => access.RequireAsync(NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.Manage, ct);

    private static bool IsAuthorized(MainMenuItemDto item, PermissionSnapshot snapshot)
    {
        var kind = item.Kind.Trim().ToLowerInvariant();

        return kind switch
        {
            NgbResourceKinds.Document => Has(snapshot, NgbResourceKinds.Document, item.Code, NgbPermissionActions.View),
            NgbResourceKinds.Catalog => Has(snapshot, NgbResourceKinds.Catalog, item.Code, NgbPermissionActions.View),
            NgbResourceKinds.Report => CanViewReport(snapshot, item.Code),
            NgbResourceKinds.Admin => IsAdminAuthorized(item, snapshot),
            NgbResourceKinds.Page when item.Route.StartsWith("/admin/security/users", StringComparison.OrdinalIgnoreCase)
                => Has(snapshot, NgbResourceKinds.System, NgbPermissionResources.Users, NgbPermissionActions.View),
            NgbResourceKinds.Page when item.Route.StartsWith("/admin/security/roles", StringComparison.OrdinalIgnoreCase)
                => Has(snapshot, NgbResourceKinds.System, NgbPermissionResources.Roles, NgbPermissionActions.View),
            NgbResourceKinds.Page when item.Route.StartsWith("/reports/", StringComparison.OrdinalIgnoreCase)
                => CanViewReport(snapshot, item.Code),
            NgbResourceKinds.Page => Has(snapshot, NgbResourceKinds.Page, item.Code, NgbPermissionActions.View),
            NgbResourceKinds.External => Has(snapshot, NgbResourceKinds.External, item.Code, NgbPermissionActions.View),
            _ => false
        };
    }

    private static bool CanViewReport(PermissionSnapshot snapshot, string reportCode)
        => Has(snapshot, NgbResourceKinds.Report, reportCode, NgbPermissionActions.View)
           || Has(snapshot, NgbResourceKinds.Report, reportCode, NgbPermissionActions.Execute);

    private static bool IsAdminAuthorized(MainMenuItemDto item, PermissionSnapshot snapshot)
    {
        if (string.Equals(item.Code, NgbPermissionResources.ChartOfAccounts, StringComparison.OrdinalIgnoreCase)
            || item.Route.StartsWith("/admin/chart-of-accounts", StringComparison.OrdinalIgnoreCase))
            return Has(snapshot, NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.View);

        if (string.Equals(item.Code, "accounting.period_closing", StringComparison.OrdinalIgnoreCase)
            || item.Route.StartsWith("/admin/accounting/period-closing", StringComparison.OrdinalIgnoreCase))
            return Has(snapshot, NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.View);

        if (string.Equals(item.Code, AccountingReportCodes.PostingLog, StringComparison.OrdinalIgnoreCase)
            || item.Route.StartsWith("/admin/accounting/posting-log", StringComparison.OrdinalIgnoreCase))
            return Has(snapshot, NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.View);

        if (string.Equals(item.Code, AccountingReportCodes.Consistency, StringComparison.OrdinalIgnoreCase)
            || item.Route.StartsWith("/admin/accounting/consistency", StringComparison.OrdinalIgnoreCase))
            return Has(snapshot, NgbResourceKinds.Admin, NgbPermissionResources.Integrity, NgbPermissionActions.View);

        return Has(snapshot, NgbResourceKinds.Admin, item.Code, NgbPermissionActions.View);
    }

    private static bool Has(PermissionSnapshot snapshot, string resourceKind, string resourceCode, string actionCode)
        => snapshot.Has(resourceKind, resourceCode, actionCode);
}
