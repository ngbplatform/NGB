using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.WorkCenter;
using NGB.Core.Events;
using NGB.Core.Security;
using NGB.Core.WorkCenter;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Outbox;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.PostgreSql.Bootstrap;
using NGB.PropertyManagement.Runtime.DocumentActions;
using NGB.PropertyManagement.Runtime.WorkCenter;
using NGB.Runtime.UnitOfWork;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.WorkCenter;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmWorkCenter_HttpAndPersistence_P0Tests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PmIntegrationFixture _fixture;

    public PmWorkCenter_HttpAndPersistence_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task WorkCenter_supports_deduplication_feed_mutations_preferences_claims_and_source_IDOR_protection()
    {
        await using var factory = new PmApiFactory(_fixture);
        using var adminClient = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var adminSubject = await GetSubjectAsync(PmKeycloakTestUsers.Admin);
        var viewerSubject = await GetSubjectAsync(PmKeycloakTestUsers.Viewer);
        var (adminUserId, viewerUserId) = await SeedUsersAndAdminRoleAsync(
            factory,
            adminSubject,
            viewerSubject);

        var sourceId = Guid.CreateVersion7();
        var taskKey = $"test:work-center:direct:{sourceId:D}";
        var notificationKey = $"test:work-center:notification:{sourceId:D}";
        Guid directTaskId;
        Guid notificationId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<IWorkCenterTaskService>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var taskRequest = TaskRequest(
                sourceId,
                taskKey,
                assignedUserId: adminUserId,
                assignedRoleCode: null,
                dueAtUtc: DateTime.UtcNow.AddHours(-1));
            directTaskId = (await tasks.CreateAsync(taskRequest, CancellationToken.None))!.Value;
            (await tasks.CreateAsync(taskRequest, CancellationToken.None)).Should().Be(directTaskId);

            var notification = Notification(sourceId, notificationKey);
            notificationId = await uow.ExecuteInUowTransactionAsync(
                ct => notifications.CreateAsync(notification, [adminUserId], ct),
                CancellationToken.None);
            (await uow.ExecuteInUowTransactionAsync(
                ct => notifications.CreateAsync(notification, [adminUserId], ct),
                CancellationToken.None)).Should().Be(notificationId);
        }

        var summary = await GetAsync<WorkCenterSummaryDto>(adminClient, "/api/work-center/summary");
        summary.AttentionCount.Should().Be(2);
        summary.OpenTaskCount.Should().Be(1);
        summary.OverdueTaskCount.Should().Be(1);
        summary.NotificationCount.Should().Be(1);
        summary.UnreadNotificationCount.Should().Be(1);

        var otherVerticalSummary = await GetAsync<WorkCenterSummaryDto>(
            adminClient,
            "/api/work-center/summary?vertical=crm");
        otherVerticalSummary.AttentionCount.Should().Be(0);
        otherVerticalSummary.OpenTaskCount.Should().Be(0);
        otherVerticalSummary.NotificationCount.Should().Be(0);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var reads = scope.ServiceProvider.GetRequiredService<IWorkCenterReadRepository>();
            var adminRoles = await scope.ServiceProvider
                .GetRequiredService<IPlatformUserRoleRepository>()
                .GetRolesForUserAsync(adminUserId);
            var taskHealth = await reads.GetTaskHealthAsync(DateTime.UtcNow, CancellationToken.None);
            taskHealth.OpenTaskCount.Should().Be(1);
            taskHealth.OverdueTaskCount.Should().Be(1);
            var rawFeed = await reads.GetItemsAsync(
                new WorkCenterQuery(
                    adminUserId,
                    adminRoles.Select(static x => x.RoleId).ToArray(),
                    AllowAllSources: true,
                    AllowedResourceKinds: [],
                    AllowedResourceCodes: [],
                    Cursor: null,
                    Limit: 2,
                    Tab: "attention",
                    Vertical: null,
                    Priority: null,
                    Severity: null,
                    Overdue: null,
                    Unread: null,
                    NowUtc: DateTime.UtcNow),
                CancellationToken.None);
            rawFeed.Should().HaveCount(2);
        }

        var firstPage = await GetAsync<WorkCenterPageDto>(
            adminClient,
            "/api/work-center/items?tab=attention&limit=1");
        firstPage.Items.Should().ContainSingle();
        firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();
        var secondPage = await GetAsync<WorkCenterPageDto>(
            adminClient,
            $"/api/work-center/items?tab=attention&limit=1&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}");
        secondPage.Items.Should().ContainSingle();
        secondPage.Items[0].Id.Should().NotBe(firstPage.Items[0].Id);

        var feed = await GetAsync<WorkCenterPageDto>(
            adminClient,
            "/api/work-center/items?tab=attention&limit=20");
        var directTask = feed.Items.Single(x => x.Id == directTaskId);
        directTask.IsOverdue.Should().BeTrue();
        directTask.PrimaryActionCode.Should()
            .Be(PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value);
        directTask.Target.Should().NotBeNull();
        feed.Items.Single(x => x.Id == notificationId).IsRead.Should().BeFalse();

        var preferences = await GetAsync<IReadOnlyList<NotificationPreferenceDto>>(
            adminClient,
            "/api/me/notification-preferences");
        var taskPreference = preferences.Single(
            x => x.Code == PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask
                 && x.Channel == NotificationChannel.InApp);
        taskPreference.Kind.Should().Be(WorkCenterPreferenceKind.Task);
        taskPreference.IsEnabled.Should().BeTrue();

        var preferenceResponse = await adminClient.PutAsJsonAsync(
            "/api/me/notification-preferences",
            new UpdateNotificationPreferencesRequestDto(
            [
                new UpdateNotificationPreferenceDto(
                    taskPreference.Code,
                    NotificationChannel.InApp,
                    IsEnabled: false)
            ]),
            JsonOptions);
        preferenceResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<IWorkCenterTaskService>();
            var suppressedTask = await tasks.CreateAsync(
                TaskRequest(
                    Guid.CreateVersion7(),
                    $"test:work-center:suppressed-task:{Guid.CreateVersion7():D}",
                    assignedUserId: null,
                    assignedRoleCode: PropertyManagementWorkCenterCodes.AccountsReceivableClerkRole,
                    dueAtUtc: DateTime.UtcNow.AddDays(1)),
                CancellationToken.None);
            suppressedTask.Should().BeNull();
        }

        (await adminClient.PostAsync(
            $"/api/work-center/notifications/{notificationId:D}/read",
            content: null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var summaryAfterNotificationRead = await GetAsync<WorkCenterSummaryDto>(
            adminClient,
            "/api/work-center/summary");
        summaryAfterNotificationRead.AttentionCount.Should().Be(1);
        summaryAfterNotificationRead.NotificationCount.Should().Be(1);
        summaryAfterNotificationRead.UnreadNotificationCount.Should().Be(0);

        (await adminClient.PostAsync(
            $"/api/work-center/notifications/{notificationId:D}/dismiss",
            content: null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await adminClient.PostAsync(
            $"/api/work-center/tasks/{directTaskId:D}/read",
            content: null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await adminClient.PostAsJsonAsync(
            $"/api/work-center/tasks/{directTaskId:D}/snooze",
            new SnoozeWorkCenterTaskRequestDto(DateTime.UtcNow.AddHours(1)),
            JsonOptions)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        summary = await GetAsync<WorkCenterSummaryDto>(adminClient, "/api/work-center/summary");
        summary.AttentionCount.Should().Be(0);
        summary.OpenTaskCount.Should().Be(1);
        summary.OverdueTaskCount.Should().Be(0);
        summary.NotificationCount.Should().Be(0);
        summary.UnreadNotificationCount.Should().Be(0);

        var attentionAfterSnooze = await GetAsync<WorkCenterPageDto>(
            adminClient,
            "/api/work-center/items?tab=attention&limit=20");
        attentionAfterSnooze.Items.Should().NotContain(x => x.Id == directTaskId);

        var tasksAfterSnooze = await GetAsync<WorkCenterPageDto>(
            adminClient,
            "/api/work-center/items?tab=tasks&limit=20");
        var snoozedTask = tasksAfterSnooze.Items.Single(x => x.Id == directTaskId);
        snoozedTask.TaskStatus.Should().Be(WorkCenterTaskStatus.Open);
        snoozedTask.SnoozedUntilUtc.Should().BeAfter(DateTime.UtcNow);

        preferenceResponse = await adminClient.PutAsJsonAsync(
            "/api/me/notification-preferences",
            new UpdateNotificationPreferencesRequestDto(
            [
                new UpdateNotificationPreferenceDto(
                    taskPreference.Code,
                    NotificationChannel.InApp,
                    IsEnabled: true)
            ]),
            JsonOptions);
        preferenceResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var roleTaskKey = $"test:work-center:role:{Guid.CreateVersion7():D}";
        Guid roleTaskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<IWorkCenterTaskService>();
            roleTaskId = (await tasks.CreateAsync(
                TaskRequest(
                    Guid.CreateVersion7(),
                    roleTaskKey,
                    assignedUserId: null,
                    assignedRoleCode: "pm-ar-clerk",
                    dueAtUtc: DateTime.UtcNow.AddDays(1)),
                CancellationToken.None))!.Value;
        }

        var claimResponse = await adminClient.PostAsJsonAsync(
            $"/api/work-center/tasks/{roleTaskId:D}/claim",
            new ClaimWorkCenterTaskRequestDto(ExpectedVersion: 1),
            JsonOptions);
        claimResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var staleClaimResponse = await adminClient.PostAsJsonAsync(
            $"/api/work-center/tasks/{roleTaskId:D}/claim",
            new ClaimWorkCenterTaskRequestDto(ExpectedVersion: 1),
            JsonOptions);
        staleClaimResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<IWorkCenterTaskService>();
            await tasks.CompleteByDeduplicationKeyAsync(roleTaskKey, CancellationToken.None);
        }
        var completed = await GetAsync<WorkCenterPageDto>(
            adminClient,
            "/api/work-center/items?tab=completed&limit=20");
        completed.Items.Single(x => x.Id == roleTaskId).TaskStatus.Should().Be(WorkCenterTaskStatus.Completed);

        var inaccessibleSourceId = Guid.CreateVersion7();
        Guid inaccessibleTaskId;
        Guid inaccessibleNotificationId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<IWorkCenterTaskRepository>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var now = DateTime.UtcNow;
            var inaccessibleTask = new WorkCenterTask(
                Guid.CreateVersion7(),
                PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
                PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
                Source(inaccessibleSourceId),
                "Review receivable payment",
                "Review the payment in receivables reconciliation.",
                WorkCenterPriority.High,
                WorkCenterTaskStatus.Open,
                AssignedUserId: viewerUserId,
                AssignedRoleId: null,
                ClaimedByUserId: null,
                DueAtUtc: null,
                PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation,
                "pm.receivables.reconciliation",
                new Dictionary<string, string?>
                {
                    ["paymentId"] = inaccessibleSourceId.ToString("D")
                },
                now,
                CompletedAtUtc: null,
                CancelledAtUtc: null,
                $"test:work-center:inaccessible-task:{inaccessibleSourceId:D}",
                Version: 1,
                CorrelationId: null,
                CausationId: null,
                MetadataJson: null);
            inaccessibleTaskId = (await uow.ExecuteInUowTransactionAsync(
                ct => tasks.CreateAsync(inaccessibleTask, [viewerUserId], ct),
                CancellationToken.None)).TaskId;
            inaccessibleNotificationId = await uow.ExecuteInUowTransactionAsync(
                ct => notifications.CreateAsync(
                    new WorkCenterNotification(
                        Guid.CreateVersion7(),
                        "pm.notification.integration_test",
                        Source(inaccessibleSourceId),
                        "Apply receivable payment",
                        "A receivable payment task was assigned.",
                        NotificationSeverity.Information,
                        DateTime.UtcNow,
                        ExpiresAtUtc: null,
                        $"test:work-center:inaccessible-notification:{inaccessibleSourceId:D}",
                        CorrelationId: null,
                        CausationId: null,
                        MetadataJson: null),
                    [viewerUserId],
                    ct),
                CancellationToken.None);
        }

        using var viewerClient = factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") },
            PmKeycloakTestUsers.Viewer);
        var viewerSummary = await GetAsync<WorkCenterSummaryDto>(
            viewerClient,
            "/api/work-center/summary");
        viewerSummary.AttentionCount.Should().Be(0);
        (await viewerClient.PostAsync(
            $"/api/work-center/tasks/{inaccessibleTaskId:D}/read",
            content: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await viewerClient.PostAsync(
            $"/api/work-center/notifications/{inaccessibleNotificationId:D}/read",
            content: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Outbox_supports_skip_locked_retry_history_completion_dead_letter_and_health()
    {
        const string consumerCode = "work-center-integration-test";
        await using var factory = new PmApiFactory(_fixture);
        var now = DateTime.UtcNow;
        var retryEvent = OutboxEvent("test.retry", now);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
            await uow.ExecuteInUowTransactionAsync(
                ct => outbox.AppendAsync(
                    retryEvent,
                    [consumerCode, consumerCode.ToUpperInvariant()],
                    ct),
                CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
            var health = await outbox.GetHealthAsync(consumerCode, CancellationToken.None);
            health.PendingCount.Should().Be(1);
            health.FailedCount.Should().Be(0);
            health.OldestOccurredAtUtc.Should().Be(retryEvent.OccurredAtUtc);
        }

        await using (var firstScope = factory.Services.CreateAsyncScope())
        await using (var secondScope = factory.Services.CreateAsyncScope())
        {
            var firstUow = firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var secondUow = secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var firstOutbox = firstScope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
            var secondOutbox = secondScope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();

            await firstUow.BeginTransactionAsync();
            var firstClaim = await firstOutbox.ClaimBatchAsync(
                consumerCode,
                batchSize: 100,
                now,
                CancellationToken.None);
            firstClaim.Should().ContainSingle();
            firstClaim[0].AttemptCount.Should().Be(1);

            await secondUow.BeginTransactionAsync();
            var concurrentClaim = await secondOutbox.ClaimBatchAsync(
                consumerCode,
                batchSize: 100,
                now,
                CancellationToken.None);
            concurrentClaim.Should().BeEmpty("SKIP LOCKED must prevent duplicate delivery");
            await secondUow.CommitAsync();

            var retryAt = now.AddMinutes(1);
            await firstOutbox.MarkFailedAsync(
                retryEvent.EventId,
                consumerCode,
                attemptNumber: 1,
                completedAtUtc: now.AddSeconds(1),
                nextAttemptAtUtc: retryAt,
                sanitizedError: "  transient\n  database timeout  ",
                deadLetter: false,
                CancellationToken.None);
            await firstUow.CommitAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
            var health = await outbox.GetHealthAsync(consumerCode, CancellationToken.None);
            health.PendingCount.Should().Be(1);
            health.FailedCount.Should().Be(1);

            await uow.BeginTransactionAsync();
            (await outbox.ClaimBatchAsync(
                consumerCode,
                batchSize: 100,
                nowUtc: now.AddSeconds(30),
                CancellationToken.None)).Should().BeEmpty();
            var retryClaim = await outbox.ClaimBatchAsync(
                consumerCode,
                batchSize: 100,
                nowUtc: now.AddMinutes(2),
                CancellationToken.None);
            retryClaim.Should().ContainSingle();
            retryClaim[0].AttemptCount.Should().Be(2);
            await outbox.MarkCompletedAsync(
                retryEvent.EventId,
                consumerCode,
                attemptNumber: 2,
                completedAtUtc: now.AddMinutes(2).AddSeconds(1),
                CancellationToken.None);
            await uow.CommitAsync();
        }

        var deadLetterEvent = OutboxEvent("test.dead_letter", now.AddMinutes(3));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
            await uow.ExecuteInUowTransactionAsync(
                ct => outbox.AppendAsync(deadLetterEvent, [consumerCode], ct),
                CancellationToken.None);
            await uow.BeginTransactionAsync();
            var claim = await outbox.ClaimBatchAsync(
                consumerCode,
                batchSize: 500,
                nowUtc: now.AddMinutes(4),
                CancellationToken.None);
            claim.Should().ContainSingle();
            await outbox.MarkFailedAsync(
                deadLetterEvent.EventId,
                consumerCode,
                attemptNumber: 1,
                completedAtUtc: now.AddMinutes(4).AddSeconds(1),
                nextAttemptAtUtc: null,
                sanitizedError: new string('x', 2_500),
                deadLetter: true,
                CancellationToken.None);
            await uow.CommitAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
            var health = await outbox.GetHealthAsync(consumerCode, CancellationToken.None);
            health.PendingCount.Should().Be(0);
            health.FailedCount.Should().Be(1);
            health.OldestOccurredAtUtc.Should().BeNull();
        }
    }

    private async Task<(Guid AdminUserId, Guid ViewerUserId)> SeedUsersAndAdminRoleAsync(
        PmApiFactory factory,
        string adminSubject,
        string viewerSubject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<PropertyManagementSecuritySeeder>()
            .EnsureSeededAsync(CancellationToken.None);

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        var userRoles = scope.ServiceProvider.GetRequiredService<IPlatformUserRoleRepository>();
        var roles = scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>();
        var arRole = await roles.GetByCodeAsync("pm-ar-clerk", CancellationToken.None);
        arRole.Should().NotBeNull();

        return await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            var adminUserId = await users.UpsertAsync(
                adminSubject,
                "pm-admin@example.test",
                "PM Admin",
                isActive: true,
                ct);
            var viewerUserId = await users.UpsertAsync(
                viewerSubject,
                "pm-viewer@example.test",
                "PM Viewer",
                isActive: true,
                ct);
            await userRoles.ReplaceUserRolesAsync(
                adminUserId,
                [arRole!.RoleId],
                assignedByUserId: adminUserId,
                ct);
            await userRoles.ReplaceUserRolesAsync(
                viewerUserId,
                [],
                assignedByUserId: adminUserId,
                ct);
            return (adminUserId, viewerUserId);
        }, CancellationToken.None);
    }

    private async Task<string> GetSubjectAsync(PmKeycloakTestUser user)
    {
        var token = await _fixture.Keycloak.GetAccessTokenAsync(user);
        return new JwtSecurityTokenHandler().ReadJwtToken(token).Subject;
    }

    private static CreateWorkCenterTaskRequest TaskRequest(
        Guid sourceId,
        string deduplicationKey,
        Guid? assignedUserId,
        string? assignedRoleCode,
        DateTime? dueAtUtc)
        => new(
            PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
            Source(sourceId),
            "Review receivable payment",
            "Review the payment in receivables reconciliation.",
            WorkCenterPriority.High,
            assignedUserId,
            assignedRoleCode,
            dueAtUtc,
            PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value,
            "pm.receivables.reconciliation",
            new Dictionary<string, string?> { ["paymentId"] = sourceId.ToString("D") },
            deduplicationKey,
            CorrelationId: null,
            CausationId: null,
            MetadataJson: null);

    private static WorkCenterNotification Notification(
        Guid sourceId,
        string deduplicationKey)
        => new(
            Guid.CreateVersion7(),
            "pm.notification.integration_test",
            Source(sourceId),
            "Payment imported",
            "A payment was imported for review.",
            NotificationSeverity.Information,
            DateTime.UtcNow,
            ExpiresAtUtc: null,
            deduplicationKey,
            CorrelationId: null,
            CausationId: null,
            MetadataJson: null);

    private static WorkCenterSourceReference Source(Guid sourceId)
        => new(
            NgbResourceKinds.Document,
            PropertyManagementCodes.ReceivablePayment,
            sourceId,
            $"Payment {sourceId:N}",
            SubtitleSnapshot: null);

    private static PlatformOutboxEvent OutboxEvent(string eventType, DateTime nowUtc)
    {
        var eventId = Guid.CreateVersion7();
        return new PlatformOutboxEvent(
            eventId,
            eventType,
            SchemaVersion: 1,
            OccurredAtUtc: nowUtc,
            Source: "ngb.integration-tests",
            Subject: $"test/{eventId:D}",
            ActorUserId: null,
            CorrelationId: Guid.CreateVersion7(),
            CausationId: null,
            PayloadJson: """{"data":{"test":true}}""",
            CreatedAtUtc: nowUtc);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string requestUri)
    {
        using var response = await client.GetAsync(requestUri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Endpoint '{requestUri}' returned an empty response.");
    }
}
