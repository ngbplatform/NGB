using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Core.Documents.Actions;
using NGB.CRM.Documents;
using NGB.CRM.Runtime.DocumentActions;
using NGB.Persistence.Security;
using NGB.Core.WorkCenter;

namespace NGB.CRM.Runtime.WorkCenter;

public sealed class CrmWorkCenterPolicy(
    IDocumentService documents,
    ICrmDocumentReaders typedDocuments,
    IWorkCenterTaskService tasks,
    INotificationService notifications,
    IPlatformRoleRepository roles,
    IPlatformUserRoleRepository userRoles,
    TimeProvider timeProvider)
    : IWorkCenterEventPolicy
{
    public string EventType => StandardDocumentActionCodes.DocumentActionCompletedType;

    public async Task HandleAsync(WorkCenterEventContext context, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(context.Event.PayloadJson);
        var data = json.RootElement.GetProperty("data");
        var documentId = data.GetProperty(StandardDocumentActionCodes.DocumentIdKey).GetGuid();
        var documentType = data.GetProperty(StandardDocumentActionCodes.DocumentType).GetString();
        var actionCode = data.GetProperty(StandardDocumentActionCodes.DocumentActionCode).GetString()?.Trim().ToLowerInvariant();

        if (string.Equals(documentType, CrmCodes.LeadIntake, StringComparison.OrdinalIgnoreCase))
        {
            if (actionCode is StandardDocumentActionCodes.PostValue or StandardDocumentActionCodes.RepostValue)
                await CreateQualificationTaskAsync(documentId, context, ct);
            else if (string.Equals(actionCode, StandardDocumentActionCodes.UnpostValue, StringComparison.OrdinalIgnoreCase))
                await tasks.CancelByDeduplicationKeyAsync(QualificationKey(documentId), ct);

            return;
        }

        if (string.Equals(documentType, CrmCodes.LeadQualification, StringComparison.OrdinalIgnoreCase)
            && actionCode is StandardDocumentActionCodes.PostValue or StandardDocumentActionCodes.RepostValue)
        {
            var qualification = await typedDocuments.ReadLeadQualificationHeadAsync(documentId, ct);
            await tasks.CompleteByDeduplicationKeyAsync(QualificationKey(qualification.LeadIntakeId), ct);

            if (string.Equals(qualification.QualificationState, "Qualified", StringComparison.OrdinalIgnoreCase))
            {
                var source = await SourceAsync(
                    CrmCodes.LeadQualification,
                    qualification.DocumentId,
                    "Lead qualification",
                    ct);

                await CreateConversionTaskAsync(qualification, source, context, ct);

                await CreateNotificationAsync(
                    CrmWorkCenterCodes.LeadQualified,
                    source,
                    "Lead qualified",
                    "The qualified lead is ready for conversion.",
                    NotificationSeverity.Success,
                    $"crm:lead:{qualification.LeadIntakeId:D}:qualified:{qualification.DocumentId:D}",
                    context,
                    ct);
            }
            else
            {
                await tasks.CancelByDeduplicationKeyAsync(ConversionKey(qualification.LeadIntakeId), ct);
            }

            return;
        }

        if (string.Equals(documentType, CrmCodes.LeadConversion, StringComparison.OrdinalIgnoreCase)
            && actionCode is StandardDocumentActionCodes.PostValue or StandardDocumentActionCodes.RepostValue)
        {
            var conversion = await typedDocuments.ReadLeadConversionHeadAsync(documentId, ct);
            await tasks.CompleteByDeduplicationKeyAsync(ConversionKey(conversion.LeadIntakeId), ct);
            return;
        }

        if (string.Equals(documentType, CrmCodes.ActivityLog, StringComparison.OrdinalIgnoreCase))
        {
            if (actionCode == StandardDocumentActionCodes.UnpostValue)
            {
                await tasks.CancelByDeduplicationKeyAsync(ActivityKey(documentId), ct);
                return;
            }

            if (actionCode is StandardDocumentActionCodes.PostValue or StandardDocumentActionCodes.RepostValue)
                await SynchronizeActivityTaskAsync(documentId, context, ct);

            return;
        }

        if (string.Equals(documentType, CrmCodes.OpportunityUpdate, StringComparison.OrdinalIgnoreCase)
            && actionCode is StandardDocumentActionCodes.PostValue or StandardDocumentActionCodes.RepostValue)
        {
            var update = await typedDocuments.ReadOpportunityUpdateHeadAsync(documentId, ct);
            if (string.Equals(update.Status, "Won", StringComparison.OrdinalIgnoreCase))
            {
                var source = await SourceAsync(
                    CrmCodes.OpportunityUpdate,
                    documentId,
                    "Opportunity update",
                    ct);

                await CreateNotificationAsync(
                    CrmWorkCenterCodes.OpportunityWon,
                    source,
                    "Opportunity won",
                    "An opportunity was moved to Won.",
                    NotificationSeverity.Success,
                    $"crm:opportunity:{update.OpportunityId:D}:won:{documentId:D}",
                    context,
                    ct);
            }
        }
    }

    private async Task CreateQualificationTaskAsync(
        Guid leadId,
        WorkCenterEventContext context,
        CancellationToken ct)
    {
        var lead = await documents.GetByIdAsync(CrmCodes.LeadIntake, leadId, ct);
        await tasks.CreateAsync(
            new CreateWorkCenterTaskRequest(
                CrmWorkCenterCodes.QualifyLeadTask,
                Source(CrmCodes.LeadIntake, leadId, lead.Display ?? lead.Number ?? "Lead intake", lead.Number),
                "Qualify lead",
                "Review the lead and create a qualification document.",
                WorkCenterPriority.High,
                AssignedUserId: null,
                AssignedRoleCode: CrmWorkCenterCodes.SalesRepresentativeRole,
                DueAtUtc: timeProvider.GetUtcNow().AddDays(2).UtcDateTime,
                PrimaryActionCode: CrmDocumentActionCodes.CreateQualification,
                NavigationTargetCode: StandardDocumentActionCodes.DocumentEditorCode,
                NavigationParameters: new Dictionary<string, string?>
                {
                    [StandardDocumentActionCodes.DocumentType] = CrmCodes.LeadIntake,
                    [StandardDocumentActionCodes.DocumentIdKey] = leadId.ToString()
                },
                QualificationKey(leadId),
                context.Event.CorrelationId,
                context.Event.EventId,
                MetadataJson: null),
            ct);
    }

    private async Task CreateConversionTaskAsync(
        CrmLeadQualificationHead qualification,
        WorkCenterSourceReference source,
        WorkCenterEventContext context,
        CancellationToken ct)
    {
        await tasks.CreateAsync(
            new CreateWorkCenterTaskRequest(
                CrmWorkCenterCodes.ConvertQualifiedLeadTask,
                source,
                "Convert qualified lead",
                "Create the account, contact, and optional opportunity conversion.",
                WorkCenterPriority.High,
                AssignedUserId: null,
                AssignedRoleCode: CrmWorkCenterCodes.SalesRepresentativeRole,
                DueAtUtc: timeProvider.GetUtcNow().AddDays(3).UtcDateTime,
                PrimaryActionCode: CrmDocumentActionCodes.CreateConversion,
                NavigationTargetCode: StandardDocumentActionCodes.DocumentEditorCode,
                NavigationParameters: new Dictionary<string, string?>
                {
                    [StandardDocumentActionCodes.DocumentType] = CrmCodes.LeadQualification,
                    [StandardDocumentActionCodes.DocumentIdKey] = qualification.DocumentId.ToString()
                },
                ConversionKey(qualification.LeadIntakeId),
                context.Event.CorrelationId,
                context.Event.EventId,
                MetadataJson: null),
            ct);
    }

    private async Task SynchronizeActivityTaskAsync(
        Guid activityId,
        WorkCenterEventContext context,
        CancellationToken ct)
    {
        var activity = await typedDocuments.ReadActivityLogHeadAsync(activityId, ct);
        if (activity.DueAtUtc is null || activity.CompletedAtUtc is not null)
        {
            await tasks.CompleteByDeduplicationKeyAsync(ActivityKey(activityId), ct);
            return;
        }

        var source = await SourceAsync(CrmCodes.ActivityLog, activityId, "CRM activity", ct);
        var priority = activity.DueAtUtc <= timeProvider.GetUtcNow().UtcDateTime
            ? WorkCenterPriority.High
            : WorkCenterPriority.Normal;

        await tasks.CreateAsync(
            new CreateWorkCenterTaskRequest(
                CrmWorkCenterCodes.CompleteActivityTask,
                source,
                "Complete CRM activity",
                activity.Subject,
                priority,
                AssignedUserId: null,
                AssignedRoleCode: CrmWorkCenterCodes.SalesRepresentativeRole,
                DueAtUtc: activity.DueAtUtc,
                PrimaryActionCode: null,
                NavigationTargetCode: StandardDocumentActionCodes.DocumentEditorCode,
                NavigationParameters: new Dictionary<string, string?>
                {
                    [StandardDocumentActionCodes.DocumentType] = CrmCodes.ActivityLog,
                    [StandardDocumentActionCodes.DocumentIdKey] = activityId.ToString()
                },
                ActivityKey(activityId),
                context.Event.CorrelationId,
                context.Event.EventId,
                MetadataJson: null),
            ct);
    }

    private async Task<WorkCenterSourceReference> SourceAsync(
        string documentType,
        Guid documentId,
        string fallbackTitle,
        CancellationToken ct)
    {
        var dto = await documents.GetByIdAsync(documentType, documentId, ct);

        return Source(
            documentType,
            documentId,
            dto.Display ?? dto.Number ?? fallbackTitle,
            dto.Number);
    }

    private async Task CreateNotificationAsync(
        string definitionCode,
        WorkCenterSourceReference source,
        string title,
        string body,
        NotificationSeverity severity,
        string deduplicationKey,
        WorkCenterEventContext context,
        CancellationToken ct)
    {
        var role = await roles.GetByCodeAsync(CrmWorkCenterCodes.SalesRepresentativeRole, ct);
        if (role is null || !role.IsActive)
            return;

        var recipients = await userRoles.GetUserIdsForRoleAsync(role.RoleId, ct);
        await notifications.CreateAsync(
            new CreateNotificationRequest(
                definitionCode,
                source,
                title,
                body,
                severity,
                recipients,
                ExpiresAtUtc: null,
                deduplicationKey,
                context.Event.CorrelationId,
                context.Event.EventId,
                MetadataJson: null),
            ct);
    }

    private static WorkCenterSourceReference Source(string code, Guid id, string title, string? subtitle)
        => new("document", code, id, title, subtitle);

    private static string QualificationKey(Guid leadId) => $"crm:lead:{leadId:D}:qualify";
    private static string ConversionKey(Guid leadId) => $"crm:lead:{leadId:D}:convert";
    private static string ActivityKey(Guid activityId) => $"crm:activity:{activityId:D}:complete";
}
