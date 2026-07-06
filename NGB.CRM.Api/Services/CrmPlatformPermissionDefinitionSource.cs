using NGB.Contracts.Security;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.CRM.Api.Services;

internal sealed class CrmPlatformPermissionDefinitionSource : INgbPermissionDefinitionSource
{
    public Task<IReadOnlyList<PermissionDefinitionDto>> GetDefinitionsAsync(CancellationToken ct)
    {
        const string systemGroup = "System";
        const string operationsGroup = "CRM Operations";

        IReadOnlyList<PermissionDefinitionDto> definitions =
        [
            Def(NgbSystemPermissions.UsersView, "View users", systemGroup),
            Def(NgbSystemPermissions.UsersManage, "Manage users", systemGroup),
            Def(NgbSystemPermissions.RolesView, "View roles", systemGroup),
            Def(NgbSystemPermissions.RolesManage, "Manage roles", systemGroup),
            Def(NgbSystemPermissions.PermissionsView, "View permission definitions", systemGroup),
            Def(NgbSystemPermissions.AuditView, "View audit log", systemGroup),
            Def(NgbResourceKinds.Page, CrmCodes.Dashboard, NgbPermissionActions.View, "View CRM dashboard", operationsGroup),
            Def(NgbResourceKinds.External, CrmCodes.Watchdog, NgbPermissionActions.View, "View CRM health", operationsGroup),
            Def(NgbResourceKinds.External, CrmCodes.BackgroundJobs, NgbPermissionActions.View, "View CRM background jobs", operationsGroup)
        ];

        return Task.FromResult(definitions);
    }

    private static PermissionDefinitionDto Def(NgbPermissionKey key, string displayName, string group)
        => new(key.ResourceKind, key.ResourceCode, key.ActionCode, displayName, group);

    private static PermissionDefinitionDto Def(
        string resourceKind,
        string resourceCode,
        string actionCode,
        string displayName,
        string group)
        => new(resourceKind, resourceCode, actionCode, displayName, group);
}
