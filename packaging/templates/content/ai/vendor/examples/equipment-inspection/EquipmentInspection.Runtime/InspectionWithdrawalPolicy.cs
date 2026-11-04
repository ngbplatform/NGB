using EquipmentInspection.Definitions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.IntegrationEvents;
using NGB.Core.Security;
using NGB.Core.WorkCenter;

namespace EquipmentInspection.Runtime;

public sealed class InspectionWithdrawalPolicy(
    IWorkCenterTaskService tasks,
    INotificationService notifications) : IDocumentActionCompletedWorkCenterPolicy
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(DocumentActionCompletedV1 @event, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (@event.Data.DocumentType != EquipmentDefinitions.InspectionCode
            || @event.Data.ActionCode != "unpost"
            || @event.ActorUserId is not { } actor)
            return [];

        var source = new WorkCenterSourceReference(NgbResourceKinds.Document, EquipmentDefinitions.InspectionCode,
            @event.Data.DocumentId, "Equipment inspection", null);
        var key = $"inspection-withdrawal:{@event.EventId:D}";
        var description = $"Review the withdrawal recorded at {@event.OccurredAtUtc:O}, document version {@event.Data.DocumentVersion}.";
        var task = await tasks.CreateAsync(new CreateWorkCenterTaskRequest(
            InspectionWorkCenterDefinitions.ReviewCode, source, "Review withdrawn inspection", description,
            WorkCenterPriority.Normal, actor, null, null, null, null, key,
            @event.CorrelationId, @event.EventId), ct);
        var notification = await notifications.CreateAsync(new CreateNotificationRequest(
            InspectionWorkCenterDefinitions.NotificationCode, source, "Inspection withdrawn", description,
            null, [actor], null, key, @event.CorrelationId, @event.EventId), ct);

        return task.ChangedUserIds.Union(notification.ChangedUserIds).ToArray();
    }
}
