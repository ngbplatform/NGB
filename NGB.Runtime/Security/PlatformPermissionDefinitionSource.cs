using NGB.Contracts.Security;
using NGB.Core.Security;

namespace NGB.Runtime.Security;

public sealed class PlatformPermissionDefinitionSource : INgbPermissionDefinitionSource
{
    public Task<IReadOnlyList<PermissionDefinitionDto>> GetDefinitionsAsync(CancellationToken ct)
    {
        const string systemGroup = "System";
        const string accountingGroup = "Accounting";
        const string adminGroup = "Admin";
        
        IReadOnlyList<PermissionDefinitionDto> definitions =
        [
            Def(NgbSystemPermissions.UsersView, "View users", systemGroup),
            Def(NgbSystemPermissions.UsersManage, "Manage users", systemGroup),
            Def(NgbSystemPermissions.RolesView, "View roles", systemGroup),
            Def(NgbSystemPermissions.RolesManage, "Manage roles", systemGroup),
            Def(NgbSystemPermissions.PermissionsView, "View permission definitions", systemGroup),
            Def(NgbSystemPermissions.AuditView, "View audit log", systemGroup),

            Def(NgbSystemPermissions.ChartOfAccountsView, "View chart of accounts", accountingGroup),
            Def(NgbSystemPermissions.ChartOfAccountsManage, "Manage chart of accounts", accountingGroup),
            Def(NgbSystemPermissions.PeriodClosingView, "View period closing", accountingGroup),
            Def(NgbSystemPermissions.PeriodClosingCloseMonth, "Close month", accountingGroup),
            Def(NgbSystemPermissions.PeriodClosingReopenMonth, "Reopen month", accountingGroup),
            Def(NgbSystemPermissions.PeriodClosingCloseFiscalYear, "Close fiscal year", accountingGroup),
            
            Def(NgbSystemPermissions.IntegrityView, "View integrity diagnostics", adminGroup),
            Def(NgbSystemPermissions.PostingLogView, "View posting log", adminGroup)
        ];

        return Task.FromResult(definitions);
    }

    private static PermissionDefinitionDto Def(NgbPermissionKey key, string displayName, string group)
        => new(key.ResourceKind, key.ResourceCode, key.ActionCode, displayName, group);
}
