using NGB.Contracts.Security;
using NGB.Core.Security;
using NGB.PropertyManagement.Definitions;
using NGB.Runtime.Security;

namespace NGB.PropertyManagement.Runtime.Security;

public sealed class PropertyManagementPermissionDefinitionSource : INgbPermissionDefinitionSource
{
    public Task<IReadOnlyList<PermissionDefinitionDto>> GetDefinitionsAsync(CancellationToken ct)
    {
        IReadOnlyList<PermissionDefinitionDto> definitions =
        [
            Page(PropertyManagementSecurityDefaults.HomePage, "Dashboard"),
            Page(PropertyManagementSecurityDefaults.ReceivablesOpenItemsPage, "Receivables Open Items"),
            Page(PropertyManagementSecurityDefaults.ReceivablesReconciliationPage, "Receivables Reconciliation"),
            Page(PropertyManagementSecurityDefaults.PayablesOpenItemsPage, "Payables Open Items"),
            Page(PropertyManagementSecurityDefaults.PayablesReconciliationPage, "Payables Reconciliation"),
            External(PropertyManagementCodes.Watchdog, "Health"),
            External(PropertyManagementCodes.BackgroundJobs, "Background Jobs")
        ];

        return Task.FromResult(definitions);
    }

    private static PermissionDefinitionDto Page(string code, string displayName)
        => new(NgbResourceKinds.Page, code, NgbPermissionActions.View, displayName, "Property Management");

    private static PermissionDefinitionDto External(string code, string displayName)
        => new(NgbResourceKinds.External, code, NgbPermissionActions.View, displayName, "Admin");
}
