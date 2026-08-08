using NGB.Core.WorkCenter;
using NGB.Definitions.WorkCenter;

namespace NGB.PropertyManagement.WorkCenter;

public static class PropertyManagementWorkCenterCodes
{
    public const string AccountsReceivableClerkRole = "pm-ar-clerk";
    public const string ApplyReceivablePaymentTask = "pm.apply_receivable_payment";
}

public sealed class PropertyManagementWorkCenterPreferenceDefinitionSource
    : IWorkCenterPreferenceDefinitionSource
{
    public IReadOnlyList<WorkCenterPreferenceDefinition> GetDefinitions()
        =>
        [
            new(
                PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
                WorkCenterPreferenceKind.Task,
                "Apply receivable payment",
                "Property Management Tasks",
                DefaultEnabled: true,
                UserCanDisable: true,
                DefaultSeverity: NotificationSeverity.Information,
                SupportedChannels: new HashSet<NotificationChannel> { NotificationChannel.InApp },
                Retention: TimeSpan.FromDays(90),
                ApplicableRoleCodes: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PropertyManagementWorkCenterCodes.AccountsReceivableClerkRole
                })
            {
                Description = "Creates a task when a posted receivable payment has credit that still needs to be applied."
            }
        ];
}
