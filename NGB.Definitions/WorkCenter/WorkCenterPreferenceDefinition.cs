using NGB.Core.WorkCenter;

namespace NGB.Definitions.WorkCenter;

/// <summary>
/// Declarative metadata describing one user-configurable Work Center task or notification.
/// </summary>
public sealed record WorkCenterPreferenceDefinition(
    string Code,
    WorkCenterPreferenceKind Kind,
    string DisplayName,
    string Category,
    bool DefaultEnabled,
    bool UserCanDisable,
    NotificationSeverity DefaultSeverity,
    IReadOnlySet<NotificationChannel> SupportedChannels,
    TimeSpan? Retention,
    string? LabelKey = null,
    bool IsMandatory = false,
    IReadOnlySet<string>? ApplicableRoleCodes = null)
{
    public string? Description { get; init; }
}

/// <summary>
/// Supplies declarative Work Center definitions to the platform registry.
/// </summary>
public interface IWorkCenterPreferenceDefinitionSource
{
    IReadOnlyList<WorkCenterPreferenceDefinition> GetDefinitions();
}
