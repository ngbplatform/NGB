using NGB.Core.WorkCenter;
using NGB.Definitions.WorkCenter;

namespace NGB.CRM.WorkCenter;

public static class CrmWorkCenterCodes
{
    public const string SalesRepresentativeRole = "crm.sales_rep";

    public const string QualifyLeadTask = "crm.qualify_lead";
    public const string ConvertQualifiedLeadTask = "crm.convert_qualified_lead";
    public const string CompleteActivityTask = "crm.complete_activity";

    public const string LeadQualified = "crm.notification.lead_qualified";
    public const string OpportunityWon = "crm.notification.opportunity_won";
}

public sealed class CrmWorkCenterPreferenceDefinitionSource : IWorkCenterPreferenceDefinitionSource
{
    private static readonly IReadOnlySet<string> SalesRepresentativeRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmWorkCenterCodes.SalesRepresentativeRole
        };

    private static readonly IReadOnlySet<NotificationChannel> InApp =
        new HashSet<NotificationChannel> { NotificationChannel.InApp };

    public IReadOnlyList<WorkCenterPreferenceDefinition> GetDefinitions()
        =>
        [
            TaskDefinition(
                CrmWorkCenterCodes.QualifyLeadTask,
                "Qualify lead",
                "Creates a task when a new lead needs qualification."),
            TaskDefinition(
                CrmWorkCenterCodes.ConvertQualifiedLeadTask,
                "Convert qualified lead",
                "Creates a task when a qualified lead is ready for conversion."),
            TaskDefinition(
                CrmWorkCenterCodes.CompleteActivityTask,
                "Complete CRM activity",
                "Creates a task when a scheduled CRM activity requires completion."),
            Informational(
                CrmWorkCenterCodes.LeadQualified,
                "Lead qualified",
                "Notifies you when a lead qualification is posted as Qualified.",
                NotificationSeverity.Success),
            Informational(
                CrmWorkCenterCodes.OpportunityWon,
                "Opportunity won",
                "Notifies you when an opportunity update is posted as Won.",
                NotificationSeverity.Success)
        ];

    private static WorkCenterPreferenceDefinition TaskDefinition(string code, string displayName, string description)
        => new(
            code,
            WorkCenterPreferenceKind.Task,
            displayName,
            "CRM Tasks",
            DefaultEnabled: true,
            UserCanDisable: true,
            DefaultSeverity: NotificationSeverity.Information,
            SupportedChannels: InApp,
            Retention: TimeSpan.FromDays(90),
            ApplicableRoleCodes: SalesRepresentativeRoles)
        {
            Description = description
        };

    private static WorkCenterPreferenceDefinition Informational(
        string code,
        string displayName,
        string description,
        NotificationSeverity severity)
        => new(
            code,
            WorkCenterPreferenceKind.Notification,
            displayName,
            "CRM Notifications",
            DefaultEnabled: true,
            UserCanDisable: true,
            DefaultSeverity: severity,
            SupportedChannels: InApp,
            Retention: TimeSpan.FromDays(90),
            ApplicableRoleCodes: SalesRepresentativeRoles)
        {
            Description = description
        };
}
