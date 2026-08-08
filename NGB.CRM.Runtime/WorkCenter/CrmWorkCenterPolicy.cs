using NGB.Application.Abstractions.Services;
using NGB.Contracts.IntegrationEvents;
using NGB.Contracts.Documents;
using NGB.Core.Documents.Actions;
using NGB.Core.Documents.Exceptions;
using NGB.Core.WorkCenter;
using NGB.CRM.Documents;
using NGB.CRM.Runtime.DocumentActions;
using NGB.CRM.WorkCenter;
using NGB.Persistence.Documents;

namespace NGB.CRM.Runtime.WorkCenter;

public sealed class CrmWorkCenterPolicy(
    IDocumentRepository documents,
    ICrmDocumentReaders typedDocuments,
    IWorkCenterTaskService tasks,
    INotificationService notifications,
    TimeProvider timeProvider)
    : IDocumentActionCompletedWorkCenterPolicy
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(DocumentActionCompletedV1 @event, CancellationToken ct)
    {
        var documentId = @event.Data.DocumentId;
        var documentType = @event.Data.DocumentType;
        var actionCode = @event.Data.ActionCode.Trim().ToLowerInvariant();

        if (string.Equals(documentType, CrmCodes.LeadIntake, StringComparison.OrdinalIgnoreCase))
        {
            if (actionCode is StandardDocumentActionCodes.PostValue or StandardDocumentActionCodes.RepostValue)
            {
                return await CreateQualificationTaskAsync(documentId, @event, ct);
            }

            if (string.Equals(actionCode, StandardDocumentActionCodes.UnpostValue, StringComparison.OrdinalIgnoreCase))
            {
                return await tasks.CancelByDeduplicationKeyAsync(
                    CrmWorkCenterCodes.QualifyLeadTask,
                    QualificationKey(documentId),
                    ct);
            }

            return [];
        }

        if (string.Equals(documentType, CrmCodes.LeadQualification, StringComparison.OrdinalIgnoreCase)
            && actionCode is StandardDocumentActionCodes.PostValue or StandardDocumentActionCodes.RepostValue)
        {
            var changedUsers = new HashSet<Guid>();
            var qualification = await typedDocuments.ReadLeadQualificationHeadAsync(documentId, ct);
            AddChanged(changedUsers, await tasks.CompleteByDeduplicationKeyAsync(
                CrmWorkCenterCodes.QualifyLeadTask,
                QualificationKey(qualification.LeadIntakeId),
                ct));

            if (string.Equals(qualification.QualificationState, "Qualified", StringComparison.OrdinalIgnoreCase))
            {
                var source = await SourceAsync(
                    CrmCodes.LeadQualification,
                    qualification.DocumentId,
                    "Lead qualification",
                    ct);

                AddChanged(changedUsers, await CreateConversionTaskAsync(qualification, source, @event, ct));

                AddChanged(changedUsers, await CreateNotificationAsync(
                    CrmWorkCenterCodes.LeadQualified,
                    source,
                    "Lead qualified",
                    "The qualified lead is ready for conversion.",
                    NotificationSeverity.Success,
                    $"crm:lead:{qualification.LeadIntakeId:D}:qualified:{qualification.DocumentId:D}",
                    @event,
                    ct));
            }
            else
            {
                AddChanged(changedUsers, await tasks.CancelByDeduplicationKeyAsync(
                    CrmWorkCenterCodes.ConvertQualifiedLeadTask,
                    ConversionKey(qualification.LeadIntakeId),
                    ct));
            }

            return changedUsers.Order().ToArray();
        }

        if (string.Equals(documentType, CrmCodes.LeadConversion, StringComparison.OrdinalIgnoreCase)
            && actionCode is StandardDocumentActionCodes.PostValue or StandardDocumentActionCodes.RepostValue)
        {
            var conversion = await typedDocuments.ReadLeadConversionHeadAsync(documentId, ct);
            return await tasks.CompleteByDeduplicationKeyAsync(
                CrmWorkCenterCodes.ConvertQualifiedLeadTask,
                ConversionKey(conversion.LeadIntakeId),
                ct);
        }

        if (string.Equals(documentType, CrmCodes.ActivityLog, StringComparison.OrdinalIgnoreCase))
        {
            if (actionCode == StandardDocumentActionCodes.UnpostValue)
            {
                return await tasks.CancelByDeduplicationKeyAsync(
                    CrmWorkCenterCodes.CompleteActivityTask,
                    ActivityKey(documentId),
                    ct);
            }

            if (actionCode is StandardDocumentActionCodes.PostValue or StandardDocumentActionCodes.RepostValue)
                return await SynchronizeActivityTaskAsync(documentId, @event, ct);

            return [];
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

                return await CreateNotificationAsync(
                    CrmWorkCenterCodes.OpportunityWon,
                    source,
                    "Opportunity won",
                    "An opportunity was moved to Won.",
                    NotificationSeverity.Success,
                    $"crm:opportunity:{update.OpportunityId:D}:won:{documentId:D}",
                    @event,
                    ct);
            }
        }

        return [];
    }

    private async Task<IReadOnlyList<Guid>> CreateQualificationTaskAsync(
        Guid leadId,
        DocumentActionCompletedV1 @event,
        CancellationToken ct)
    {
        var lead = await typedDocuments.ReadLeadIntakeHeadAsync(leadId, ct);
        var result = await tasks.CreateAsync(
            new CreateWorkCenterTaskRequest(
                CrmWorkCenterCodes.QualifyLeadTask,
                Source(CrmCodes.LeadIntake, leadId, lead.LeadName, lead.CompanyName),
                "Qualify lead",
                "Review the lead and create a qualification document.",
                WorkCenterPriority.High,
                AssignedUserId: null,
                AssignedRoleCode: CrmWorkCenterCodes.SalesRepresentativeRole,
                DueAtUtc: timeProvider.GetUtcNow().AddDays(2).UtcDateTime,
                PrimaryActionCode: new DocumentActionCode(CrmDocumentActionCodes.CreateQualification),
                Target: new DocumentActionTargetDto(
                    StandardDocumentTargets.Editor,
                    new Dictionary<string, string?>
                    {
                        [StandardDocumentTargetParameters.DocumentType] = CrmCodes.LeadIntake,
                        [StandardDocumentTargetParameters.DocumentId] = leadId.ToString()
                    }),
                QualificationKey(leadId),
                @event.CorrelationId,
                @event.EventId),
            ct);

        return result.ChangedUserIds;
    }

    private async Task<IReadOnlyList<Guid>> CreateConversionTaskAsync(
        CrmLeadQualificationHead qualification,
        WorkCenterSourceReference source,
        DocumentActionCompletedV1 @event,
        CancellationToken ct)
    {
        var result = await tasks.CreateAsync(
            new CreateWorkCenterTaskRequest(
                CrmWorkCenterCodes.ConvertQualifiedLeadTask,
                source,
                "Convert qualified lead",
                "Create the account, contact, and optional opportunity conversion.",
                WorkCenterPriority.High,
                AssignedUserId: null,
                AssignedRoleCode: CrmWorkCenterCodes.SalesRepresentativeRole,
                DueAtUtc: timeProvider.GetUtcNow().AddDays(3).UtcDateTime,
                PrimaryActionCode: new DocumentActionCode(CrmDocumentActionCodes.CreateConversion),
                Target: new DocumentActionTargetDto(
                    StandardDocumentTargets.Editor,
                    new Dictionary<string, string?>
                    {
                        [StandardDocumentTargetParameters.DocumentType] = CrmCodes.LeadQualification,
                        [StandardDocumentTargetParameters.DocumentId] = qualification.DocumentId.ToString()
                    }),
                ConversionKey(qualification.LeadIntakeId),
                @event.CorrelationId,
                @event.EventId),
            ct);

        return result.ChangedUserIds;
    }

    private async Task<IReadOnlyList<Guid>> SynchronizeActivityTaskAsync(
        Guid activityId,
        DocumentActionCompletedV1 @event,
        CancellationToken ct)
    {
        var activity = await typedDocuments.ReadActivityLogHeadAsync(activityId, ct);
        if (activity.DueAtUtc is null || activity.CompletedAtUtc is not null)
        {
            return await tasks.CompleteByDeduplicationKeyAsync(
                CrmWorkCenterCodes.CompleteActivityTask,
                ActivityKey(activityId),
                ct);
        }

        var source = Source(CrmCodes.ActivityLog, activityId, activity.Subject, activity.ActivityType);
        var dueAtUtc = activity.DueAtUtc.Value;
        var priority = dueAtUtc <= timeProvider.GetUtcNow().UtcDateTime
            ? WorkCenterPriority.High
            : WorkCenterPriority.Normal;

        var result = await tasks.CreateAsync(
            new CreateWorkCenterTaskRequest(
                CrmWorkCenterCodes.CompleteActivityTask,
                source,
                "Complete CRM activity",
                activity.Subject,
                priority,
                AssignedUserId: null,
                AssignedRoleCode: CrmWorkCenterCodes.SalesRepresentativeRole,
                DueAtUtc: dueAtUtc,
                PrimaryActionCode: null,
                Target: new DocumentActionTargetDto(
                    StandardDocumentTargets.Editor,
                    new Dictionary<string, string?>
                    {
                        [StandardDocumentTargetParameters.DocumentType] = CrmCodes.ActivityLog,
                        [StandardDocumentTargetParameters.DocumentId] = activityId.ToString()
                    }),
                ActivityKey(activityId),
                @event.CorrelationId,
                @event.EventId),
            ct);

        return result.ChangedUserIds;
    }

    private async Task<WorkCenterSourceReference> SourceAsync(
        string documentType,
        Guid documentId,
        string fallbackTitle,
        CancellationToken ct)
    {
        var document = await documents.GetAsync(documentId, ct)
            ?? throw new DocumentNotFoundException(documentId);

        if (!string.Equals(document.TypeCode, documentType, StringComparison.OrdinalIgnoreCase))
            throw new DocumentTypeMismatchException(documentId, documentType, document.TypeCode);

        return Source(
            documentType,
            documentId,
            document.Number ?? fallbackTitle,
            document.Number);
    }

    private async Task<IReadOnlyList<Guid>> CreateNotificationAsync(
        string definitionCode,
        WorkCenterSourceReference source,
        string title,
        string body,
        NotificationSeverity severity,
        string deduplicationKey,
        DocumentActionCompletedV1 @event,
        CancellationToken ct)
    {
        var result = await notifications.CreateAsync(
            new CreateNotificationRequest(
                definitionCode,
                source,
                title,
                body,
                severity,
                RecipientUserIds: [],
                ExpiresAtUtc: null,
                deduplicationKey,
                @event.CorrelationId,
                @event.EventId)
            {
                RecipientRoleCode = CrmWorkCenterCodes.SalesRepresentativeRole
            },
            ct);

        return result.ChangedUserIds;
    }

    private static void AddChanged(ISet<Guid> target, IEnumerable<Guid> userIds)
    {
        foreach (var userId in userIds)
        {
            if (userId != Guid.Empty)
                target.Add(userId);
        }
    }

    private static WorkCenterSourceReference Source(string code, Guid id, string title, string? subtitle)
        => new("document", code, id, title, subtitle);

    private static string QualificationKey(Guid leadId) => $"crm:lead:{leadId:D}:qualify";
    private static string ConversionKey(Guid leadId) => $"crm:lead:{leadId:D}:convert";
    private static string ActivityKey(Guid activityId) => $"crm:activity:{activityId:D}:complete";
}
