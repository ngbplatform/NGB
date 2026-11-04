using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.IntegrationEvents;
using NGB.Contracts.Metadata;
using NGB.Core.Security;
using NGB.Core.WorkCenter;
using Xunit;

namespace EquipmentInspection.Tests;

public sealed class InspectionWithdrawalTests
{
    [Theory]
    [InlineData("example.other", "unpost", true)]
    [InlineData(EquipmentDefinitions.InspectionCode, "post", true)]
    [InlineData(EquipmentDefinitions.InspectionCode, "unpost", false)]
    public async Task Unrelated_actions_and_events_without_a_personal_identity_do_not_create_work(
        string documentType, string action, bool linkedActor)
    {
        var writes = new WorkCenterWrites();
        var policy = new InspectionWithdrawalPolicy(writes, writes);
        var completed = Event() with
        {
            ActorUserId = linkedActor ? Guid.CreateVersion7() : null,
            Data = Event().Data with { DocumentType = documentType, ActionCode = action }
        };

        Assert.Empty(await policy.HandleAsync(completed, CancellationToken.None));
        Assert.Null(writes.TaskRequest);
        Assert.Null(writes.NotificationRequest);
    }

    [Fact]
    public async Task Withdrawal_preserves_event_identity_source_and_distinct_changed_users()
    {
        var writes = new WorkCenterWrites();
        var completed = Event();
        var changed = await new InspectionWithdrawalPolicy(writes, writes).HandleAsync(completed, CancellationToken.None);

        Assert.Equal(new[] { writes.First, writes.Second }.Order(), changed.Order());
        var task = Assert.IsType<CreateWorkCenterTaskRequest>(writes.TaskRequest);
        var notification = Assert.IsType<CreateNotificationRequest>(writes.NotificationRequest);
        Assert.Equal(InspectionWorkCenterDefinitions.ReviewCode, task.TaskCode);
        Assert.Equal(InspectionWorkCenterDefinitions.NotificationCode, notification.DefinitionCode);
        Assert.Equal("inspection-withdrawal:" + completed.EventId.ToString("D"), task.DeduplicationKey);
        Assert.Equal(task.DeduplicationKey, notification.DeduplicationKey);
        Assert.Equal(completed.CorrelationId, task.CorrelationId);
        Assert.Equal(completed.CorrelationId, notification.CorrelationId);
        Assert.Equal(completed.EventId, task.CausationId);
        Assert.Equal(completed.EventId, notification.CausationId);
        Assert.Equal(NgbResourceKinds.Document, task.Source.ResourceKind);
        Assert.Equal(EquipmentDefinitions.InspectionCode, task.Source.ResourceCode);
        Assert.Equal(completed.Data.DocumentId, task.Source.EntityId);
        Assert.Equal(task.Source, notification.Source);
        Assert.Equal(completed.ActorUserId, task.AssignedUserId);
        Assert.Null(task.AssignedRoleCode);
        Assert.Equal(completed.ActorUserId, Assert.Single(notification.RecipientUserIds));
        Assert.Contains("document version 8", task.Description);
        Assert.Equal(task.Description, notification.Body);
        Assert.Null(task.PrimaryActionCode);
        Assert.Null(task.Target);
    }

    [Fact]
    public async Task Cancellation_stops_before_any_work_center_mutation()
    {
        var writes = new WorkCenterWrites();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new InspectionWithdrawalPolicy(writes, writes).HandleAsync(Event(), cancellation.Token));
        Assert.Null(writes.TaskRequest);
        Assert.Null(writes.NotificationRequest);
    }

    [Fact]
    public void Review_is_mandatory_and_informational_notification_can_be_disabled()
    {
        var definitions = new InspectionWorkCenterDefinitions().GetDefinitions();
        Assert.Equal(2, definitions.Count);
        var review = Assert.Single(definitions, item => item.Code == InspectionWorkCenterDefinitions.ReviewCode);
        var notification = Assert.Single(definitions, item => item.Code == InspectionWorkCenterDefinitions.NotificationCode);
        Assert.Equal(WorkCenterPreferenceKind.Task, review.Kind);
        Assert.True(review.IsMandatory);
        Assert.False(review.UserCanDisable);
        Assert.Null(review.Retention);
        Assert.Equal(WorkCenterPreferenceKind.Notification, notification.Kind);
        Assert.False(notification.IsMandatory);
        Assert.True(notification.UserCanDisable);
        Assert.Equal(TimeSpan.FromDays(30), notification.Retention);
        Assert.All(definitions, item =>
        {
            Assert.True(item.DefaultEnabled);
            Assert.Equal(NotificationChannel.InApp, Assert.Single(item.SupportedChannels));
        });
    }

    private static DocumentActionCompletedV1 Event() => new(Guid.CreateVersion7(),
        new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc), "equipment-inspection", "document:test",
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        new DocumentActionCompletedDataV1(Guid.CreateVersion7(), EquipmentDefinitions.InspectionCode, "unpost",
            DocumentStatus.Posted, DocumentStatus.Draft, 8));

    private sealed class WorkCenterWrites : IWorkCenterTaskService, INotificationService
    {
        public Guid First { get; } = Guid.CreateVersion7();
        public Guid Second { get; } = Guid.CreateVersion7();
        public CreateWorkCenterTaskRequest? TaskRequest { get; private set; }
        public CreateNotificationRequest? NotificationRequest { get; private set; }

        public Task<WorkCenterMutationResult> CreateAsync(CreateWorkCenterTaskRequest request, CancellationToken ct)
        {
            TaskRequest = request;
            return Task.FromResult(new WorkCenterMutationResult(Guid.CreateVersion7(), [First]));
        }

        public Task<WorkCenterMutationResult> CreateAsync(CreateNotificationRequest request, CancellationToken ct)
        {
            NotificationRequest = request;
            return Task.FromResult(new WorkCenterMutationResult(Guid.CreateVersion7(), [First, Second]));
        }

        public Task<IReadOnlyList<Guid>> CompleteByDeduplicationKeyAsync(string code, string key, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> CompleteByDeduplicationKeysAsync(string code, IReadOnlyCollection<string> keys, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> CancelByDeduplicationKeyAsync(string code, string key, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
