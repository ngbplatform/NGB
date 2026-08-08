using FluentAssertions;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Core.WorkCenter;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Actions;
using NGB.Persistence.Outbox;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.PostgreSql.Bootstrap;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.WorkCenter;

[Collection(PmIntegrationCollection.Name)]
public sealed class PostgresWorkCenterRepositoryCoverageTests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Retention_prunes_each_terminal_resource_in_one_bounded_database_batch()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var uow = services.GetRequiredService<IUnitOfWork>();
        var documents = services.GetRequiredService<IDocumentRepository>();
        var executions = services.GetRequiredService<IDocumentActionExecutionRepository>();
        var tasks = services.GetRequiredService<IWorkCenterTaskRepository>();
        var notifications = services.GetRequiredService<INotificationRepository>();
        var outbox = services.GetRequiredService<IOutboxEventRepository>();
        var maintenance = services.GetRequiredService<IWorkCenterMaintenanceRepository>();
        var users = services.GetRequiredService<IPlatformUserRepository>();
        var old = DateTime.UtcNow.AddDays(-100);
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var documentId = Guid.CreateVersion7();
        var outboxEventId = Guid.CreateVersion7();
        const string consumerCode = "retention-integration-test";
        var recipientId = await uow.ExecuteInUowTransactionAsync(
            ct => users.UpsertAsync(
                $"work-center-retention-{Guid.NewGuid():N}",
                email: null,
                displayName: "Work Center Retention",
                isActive: true,
                ct),
            CancellationToken.None);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await documents.CreateAsync(
                new DocumentRecord
                {
                    Id = documentId,
                    TypeCode = "it.retention",
                    Number = "RET-1",
                    DateUtc = old,
                    Status = DocumentStatus.Draft,
                    Version = 1,
                    CreatedAtUtc = old,
                    UpdatedAtUtc = old,
                    PostedAtUtc = null,
                    MarkedForDeletionAtUtc = null
                },
                ct);

            var execution = await executions.TryBeginAsync(
                $"retention-{Guid.NewGuid():N}",
                new string('a', 64),
                documentId,
                "it.retention",
                "post",
                old,
                ct);
            execution.Status.Should().Be(DocumentActionExecutionBeginStatus.Begun);
            await executions.MarkCompletedAsync(execution.ExecutionId, "{}", old.AddMinutes(1), ct);

            var taskId = Guid.CreateVersion7();
            await tasks.CreateAsync(
                new WorkCenterTask(
                    taskId,
                    "test.retention",
                    PreferenceCode: null,
                    Source(documentId),
                    "Expired task",
                    Description: null,
                    WorkCenterPriority.Normal,
                    WorkCenterTaskStatus.Completed,
                    AssignedUserId: recipientId,
                    AssignedRoleId: null,
                    ClaimedByUserId: null,
                    DueAtUtc: null,
                    CreatedAtUtc: old,
                    CompletedAtUtc: old.AddMinutes(1),
                    CancelledAtUtc: null,
                    DeduplicationKey: $"retention:{taskId:D}",
                    Version: 1,
                    CorrelationId: null,
                    CausationId: null),
                primaryActionCode: null,
                target: null,
                [recipientId],
                ct);

            var notificationId = Guid.CreateVersion7();
            await notifications.CreateAsync(
                new WorkCenterNotification(
                    notificationId,
                    "test.retention",
                    Source(documentId),
                    "Expired notification",
                    Body: null,
                    NotificationSeverity.Information,
                    CreatedAtUtc: old,
                    ExpiresAtUtc: old.AddDays(1),
                    DeduplicationKey: $"retention:{notificationId:D}",
                    CorrelationId: null,
                    CausationId: null),
                [recipientId],
                ct);

            await outbox.AppendAsync(
                new OutboxEventEnvelope(
                    outboxEventId,
                    "test.retention",
                    SchemaVersion: 1,
                    OccurredAtUtc: old,
                    Source: "tests",
                    Subject: $"retention:{outboxEventId:D}",
                    ActorUserId: null,
                    CorrelationId: Guid.CreateVersion7(),
                    CausationId: null,
                    PayloadJson: "{}",
                    CreatedAtUtc: old),
                [consumerCode],
                ct);
        }, CancellationToken.None);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            var claimed = await outbox.ClaimBatchAsync(consumerCode, 1, cutoff, ct);
            claimed.Should().ContainSingle(x => x.Event.EventId == outboxEventId);
            await outbox.MarkCompletedAsync(outboxEventId, consumerCode, 1, cutoff, ct);
        }, CancellationToken.None);

        var cutoffs = new WorkCenterRetentionCutoffs(cutoff, cutoff, cutoff, cutoff);
        var pruned = await uow.ExecuteInUowTransactionAsync(
            ct => maintenance.PruneAsync(cutoffs, batchSize: 100, ct),
            CancellationToken.None);

        pruned.Should().Be(new WorkCenterPruneResult(
            DocumentActionExecutions: 1,
            Tasks: 1,
            NotificationDeliveries: 1,
            Notifications: 1,
            OutboxEvents: 1));

        var secondPass = await uow.ExecuteInUowTransactionAsync(
            ct => maintenance.PruneAsync(cutoffs, batchSize: 100, ct),
            CancellationToken.None);
        secondPass.Total.Should().Be(0);
    }

    [Fact]
    public async Task Covers_empty_preferences_tab_normalization_cancel_and_assignment_validation()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var tasks = scope.ServiceProvider.GetRequiredService<IWorkCenterTaskRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var maintenance = scope.ServiceProvider.GetRequiredService<IWorkCenterMaintenanceRepository>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
        var preferences = scope.ServiceProvider.GetRequiredService<INotificationPreferenceRepository>();
        var reads = scope.ServiceProvider.GetRequiredService<IWorkCenterReadRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        var now = DateTime.UtcNow;
        var recipientId = await uow.ExecuteInUowTransactionAsync(
            ct => users.UpsertAsync(
                $"work-center-repository-coverage-{Guid.NewGuid():N}",
                email: null,
                displayName: "Work Center Repository Coverage",
                isActive: true,
                ct),
            CancellationToken.None);

        await FluentActions.Awaiting(() => tasks.CreateAsync(
                InvalidTask(now),
                null,
                null,
                [recipientId],
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        await scope.ServiceProvider
            .GetRequiredService<PropertyManagementSecuritySeeder>()
            .EnsureSeededAsync(CancellationToken.None);
        var role = await scope.ServiceProvider
            .GetRequiredService<IPlatformRoleRepository>()
            .GetByCodeAsync("pm-ar-clerk", CancellationToken.None);
        role.Should().NotBeNull();
        var task = ValidTask(now, role!.RoleId);
        var taskTarget = new WorkCenterNavigationTargetRecord(
            "document.editor",
            new Dictionary<string, string?> { ["documentId"] = task.Source.EntityId.ToString("D") });

        foreach (var invalidBatchSize in new[] { 0, 10_001 })
        {
            await FluentActions.Awaiting(() => maintenance.PruneAsync(
                    new WorkCenterRetentionCutoffs(now, now, now, now),
                    invalidBatchSize,
                    CancellationToken.None))
                .Should().ThrowAsync<NgbArgumentInvalidException>();
        }

        foreach (var recipients in new IReadOnlyList<Guid>[] { [], [Guid.Empty] })
        {
            await FluentActions.Awaiting(() => tasks.CreateAsync(
                    task,
                    null,
                    null,
                    recipients,
                    CancellationToken.None))
                .Should().ThrowAsync<NgbArgumentInvalidException>();
        }

        await preferences.UpsertManyAsync([], CancellationToken.None);
        var preference = new NotificationPreferenceRecord(
            recipientId,
            "test.valid",
            NotificationChannel.InApp,
            IsEnabled: false,
            UpdatedAtUtc: now,
            Version: 1);
        await uow.ExecuteInUowTransactionAsync(
            ct => preferences.UpsertAsync(preference, ct),
            CancellationToken.None);
        (await preferences.GetForUserAsync(recipientId, CancellationToken.None))
            .Should().ContainSingle()
            .Which.Should().BeEquivalentTo(preference, options => options.Excluding(x => x.Version));

        var created = await uow.ExecuteInUowTransactionAsync(
            ct => tasks.CreateAsync(task, "open", taskTarget, [recipientId], ct),
            CancellationToken.None);
        created.Should().Be(new WorkCenterTaskCreateResult(task.Id, BecameActive: true, Version: 1));
        var duplicate = await uow.ExecuteInUowTransactionAsync(
            ct => tasks.CreateAsync(task, null, null, [recipientId], ct),
            CancellationToken.None);
        duplicate.Should().Be(new WorkCenterTaskCreateResult(task.Id, BecameActive: false, Version: 1));
        await uow.ExecuteInUowTransactionAsync(
            ct => tasks.CompleteByDeduplicationKeyAsync(task.TaskCode, task.DeduplicationKey, now.AddSeconds(1), ct),
            CancellationToken.None);
        var reopened = await uow.ExecuteInUowTransactionAsync(
            ct => tasks.CreateAsync(
                task with { CreatedAtUtc = now.AddSeconds(2) },
                null,
                null,
                [recipientId],
                ct),
            CancellationToken.None);
        reopened.Should().Be(new WorkCenterTaskCreateResult(task.Id, BecameActive: true, Version: 3));

        var claimed = await uow.ExecuteInUowTransactionAsync(
            ct => tasks.ClaimAsync(
                task.Id,
                recipientId,
                [role.RoleId],
                allowAllSources: true,
                allowedResourceKinds: [],
                allowedResourceCodes: [],
                expectedVersion: reopened.Version,
                claimedAtUtc: now.AddSeconds(3),
                ct),
            CancellationToken.None);
        claimed.Should().BeTrue();

        var duplicateAfterClaim = await uow.ExecuteInUowTransactionAsync(
            ct => tasks.CreateAsync(
                task with { CreatedAtUtc = now.AddSeconds(4) },
                primaryActionCode: "open",
                target: taskTarget,
                [recipientId],
                ct),
            CancellationToken.None);
        duplicateAfterClaim.Should().Be(new WorkCenterTaskCreateResult(task.Id, BecameActive: false, Version: 4));

        var notification = new WorkCenterNotification(
            Guid.NewGuid(),
            "test.valid.notification",
            task.Source,
            "Valid notification",
            Body: null,
            NotificationSeverity.Warning,
            CreatedAtUtc: now.AddSeconds(5),
            ExpiresAtUtc: null,
            DeduplicationKey: $"notification:{Guid.NewGuid():D}",
            CorrelationId: null,
            CausationId: null);
        await uow.ExecuteInUowTransactionAsync(
            ct => notifications.CreateAsync(notification, [recipientId], ct),
            CancellationToken.None);

        var taskItems = await reads.GetItemsAsync(
            Query(now.AddMinutes(1), WorkCenterQueryView.Tasks, recipientId, [role.RoleId]),
            CancellationToken.None);
        taskItems.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Kind = WorkCenterItemKind.Task,
            Priority = (WorkCenterPriority?)WorkCenterPriority.Normal,
            Severity = (NotificationSeverity?)null,
            TaskStatus = (WorkCenterTaskStatus?)WorkCenterTaskStatus.InProgress,
            ClaimedByUserId = (Guid?)recipientId,
            PrimaryActionCode = "open",
            Target = taskTarget
        });

        await uow.ExecuteInUowTransactionAsync(
            ct => uow.Connection.ExecuteAsync(new CommandDefinition(
                "UPDATE platform_tasks SET navigation_parameters_json = 'null'::jsonb WHERE id = @TaskId;",
                new { TaskId = task.Id },
                uow.Transaction,
                cancellationToken: ct)),
            CancellationToken.None);
        var nullTargetParameters = await reads.GetItemsAsync(
            Query(now.AddMinutes(1), WorkCenterQueryView.Tasks, recipientId, [role.RoleId]),
            CancellationToken.None);
        nullTargetParameters.Should().ContainSingle().Which.Target!.Parameters.Should().BeEmpty();

        var notificationItems = await reads.GetItemsAsync(
            Query(now.AddMinutes(1), WorkCenterQueryView.Notifications, recipientId, [role.RoleId]),
            CancellationToken.None);
        notificationItems.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Kind = WorkCenterItemKind.Notification,
            Priority = (WorkCenterPriority?)null,
            Severity = (NotificationSeverity?)NotificationSeverity.Warning,
            TaskStatus = (WorkCenterTaskStatus?)null
        });

        (await preferences.GetForUsersAsync([], CancellationToken.None)).Should().BeEmpty();

        foreach (var query in new[]
                 {
                     Query(now, WorkCenterQueryView.Tasks),
                     Query(now, WorkCenterQueryView.Notifications),
                     Query(now, WorkCenterQueryView.Attention),
                     Query(
                         now,
                         WorkCenterQueryView.Completed,
                         cursor: new WorkCenterCursor(now.AddMinutes(1), Guid.NewGuid()),
                         priority: WorkCenterPriority.High,
                         severity: NotificationSeverity.Warning)
                 })
        {
            var result = await reads.GetItemsAsync(query, CancellationToken.None);
            result.Should().BeEmpty();
        }

        await uow.ExecuteInUowTransactionAsync(
            ct => tasks.CancelByDeduplicationKeyAsync(task.TaskCode, "missing-task", now, ct),
            CancellationToken.None);

        var outboxEvent = new OutboxEventEnvelope(
            Guid.NewGuid(),
            "test.event",
            1,
            now,
            "tests",
            "test",
            null,
            Guid.NewGuid(),
            null,
            "{}",
            now);
        await FluentActions.Awaiting(() => outbox.AppendAsync(
                null!,
                ["consumer"],
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await FluentActions.Awaiting(() => outbox.AppendAsync(
                outboxEvent,
                null!,
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await FluentActions.Awaiting(() => outbox.AppendAsync(
                outboxEvent,
                [],
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await FluentActions.Awaiting(() => uow.ExecuteInUowTransactionAsync(
                ct => outbox.AppendAsync(outboxEvent, [" ", "\t"], ct),
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await FluentActions.Awaiting(() => outbox.ClaimBatchAsync(
                " ",
                1,
                now,
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        foreach (var invalidBatchSize in new[] { 0, 501 })
        {
            await FluentActions.Awaiting(() => outbox.ClaimBatchAsync(
                    "consumer",
                    invalidBatchSize,
                    now,
                    CancellationToken.None))
                .Should().ThrowAsync<NgbArgumentInvalidException>();
        }

        await uow.BeginTransactionAsync();
        await FluentActions.Awaiting(() => outbox.MarkCompletedAsync(
                Guid.NewGuid(),
                "consumer",
                1,
                now,
                CancellationToken.None))
            .Should().ThrowAsync<NgbInvariantViolationException>();
        await FluentActions.Awaiting(() => outbox.MarkFailedAsync(
                Guid.NewGuid(),
                "consumer",
                1,
                now,
                nextAttemptAtUtc: null,
                sanitizedError: null!,
                deadLetter: false,
                CancellationToken.None))
            .Should().ThrowAsync<NgbInvariantViolationException>();
        await uow.RollbackAsync();
    }

    private static WorkCenterQuery Query(
        DateTime now,
        WorkCenterQueryView view,
        Guid? userId = null,
        IReadOnlyList<Guid>? roleIds = null,
        WorkCenterCursor? cursor = null,
        WorkCenterPriority? priority = null,
        NotificationSeverity? severity = null)
        => new(
            userId ?? Guid.NewGuid(),
            roleIds ?? [],
            AllowAllSources: true,
            AllowedResourceKinds: [],
            AllowedResourceCodes: [],
            Cursor: cursor,
            Limit: 10,
            View: view,
            Vertical: null,
            Priority: priority,
            Severity: severity,
            Overdue: null,
            Unread: null,
            NowUtc: now);

    private static WorkCenterSourceReference Source(Guid entityId)
        => new(
            "document",
            "it.retention",
            entityId,
            "Retention source",
            SubtitleSnapshot: null);

    private static WorkCenterTask ValidTask(DateTime now, Guid roleId)
        => new(
            Guid.NewGuid(),
            "test.valid",
            PreferenceCode: null,
            new WorkCenterSourceReference(
                "document",
                "pm.receivable_payment",
                Guid.NewGuid(),
                "Payment",
                null),
            "Valid assignment",
            null,
            WorkCenterPriority.Normal,
            WorkCenterTaskStatus.Open,
            AssignedUserId: null,
            AssignedRoleId: roleId,
            ClaimedByUserId: null,
            DueAtUtc: null,
            CreatedAtUtc: now,
            CompletedAtUtc: null,
            CancelledAtUtc: null,
            DeduplicationKey: $"valid:{Guid.NewGuid():D}",
            Version: 1,
            CorrelationId: null,
            CausationId: null);

    private static WorkCenterTask InvalidTask(DateTime now)
        => new(
            Guid.NewGuid(),
            "test.invalid",
            PreferenceCode: null,
            new WorkCenterSourceReference(
                "document",
                "pm.receivable_payment",
                Guid.NewGuid(),
                "Payment",
                null),
            "Invalid assignment",
            null,
            WorkCenterPriority.Normal,
            WorkCenterTaskStatus.Open,
            AssignedUserId: null,
            AssignedRoleId: null,
            ClaimedByUserId: null,
            DueAtUtc: null,
            CreatedAtUtc: now,
            CompletedAtUtc: null,
            CancelledAtUtc: null,
            DeduplicationKey: "invalid",
            Version: 1,
            CorrelationId: null,
            CausationId: null);
}
