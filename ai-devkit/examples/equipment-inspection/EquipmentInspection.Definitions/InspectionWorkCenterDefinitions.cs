using NGB.Core.WorkCenter;
using NGB.Definitions.WorkCenter;

namespace EquipmentInspection.Definitions;

public sealed class InspectionWorkCenterDefinitions : IWorkCenterPreferenceDefinitionSource
{
    public const string ReviewCode = "example.inspection-withdrawal-review";
    public const string NotificationCode = "example.inspection-withdrawn";

    public IReadOnlyList<WorkCenterPreferenceDefinition> GetDefinitions() =>
    [
        new(ReviewCode, WorkCenterPreferenceKind.Task, "Withdrawn inspection reviews", "Equipment", true, false,
            NotificationSeverity.Warning, new HashSet<NotificationChannel> { NotificationChannel.InApp }, null,
            IsMandatory: true),
        new(NotificationCode, WorkCenterPreferenceKind.Notification, "Inspection withdrawals", "Equipment", true, true,
            NotificationSeverity.Information, new HashSet<NotificationChannel> { NotificationChannel.InApp }, TimeSpan.FromDays(30))
    ];
}
