using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Events;
using NGB.Core.WorkCenter;
using NGB.Persistence.AuditLog;
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
    public async Task Covers_empty_preferences_tab_normalization_cancel_and_assignment_validation()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var tasks = scope.ServiceProvider.GetRequiredService<IWorkCenterTaskRepository>();
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
        var created = await uow.ExecuteInUowTransactionAsync(
            ct => tasks.CreateAsync(task, [recipientId], ct),
            CancellationToken.None);
        created.Should().Be(new WorkCenterTaskCreateResult(task.Id, BecameActive: true, Version: 1));
        var duplicate = await uow.ExecuteInUowTransactionAsync(
            ct => tasks.CreateAsync(task, [recipientId], ct),
            CancellationToken.None);
        duplicate.Should().Be(new WorkCenterTaskCreateResult(task.Id, BecameActive: false, Version: 1));
        await uow.ExecuteInUowTransactionAsync(
            ct => tasks.CompleteByDeduplicationKeyAsync(task.DeduplicationKey, now.AddSeconds(1), ct),
            CancellationToken.None);
        var reopened = await uow.ExecuteInUowTransactionAsync(
            ct => tasks.CreateAsync(
                task with { CreatedAtUtc = now.AddSeconds(2) },
                [recipientId],
                ct),
            CancellationToken.None);
        reopened.Should().Be(new WorkCenterTaskCreateResult(task.Id, BecameActive: true, Version: 3));

        (await preferences.GetForUsersAsync([], CancellationToken.None)).Should().BeEmpty();

        foreach (var query in new[]
                 {
                     Query(now, tab: " TASKS "),
                     Query(now, tab: "notifications"),
                     Query(now, tab: null),
                     Query(
                         now,
                         tab: "completed",
                         cursor: new WorkCenterCursor(now.AddMinutes(1), Guid.NewGuid()),
                         priority: WorkCenterPriority.High,
                         severity: NotificationSeverity.Warning)
                 })
        {
            var result = await reads.GetItemsAsync(query, CancellationToken.None);
            result.Should().BeEmpty();
        }

        await uow.ExecuteInUowTransactionAsync(
            ct => tasks.CancelByDeduplicationKeyAsync("missing-task", now, ct),
            CancellationToken.None);

        var outboxEvent = new PlatformOutboxEvent(
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
        string? tab,
        WorkCenterCursor? cursor = null,
        WorkCenterPriority? priority = null,
        NotificationSeverity? severity = null)
        => new(
            Guid.NewGuid(),
            [],
            AllowAllSources: true,
            AllowedResourceKinds: [],
            AllowedResourceCodes: [],
            Cursor: cursor,
            Limit: 10,
            Tab: tab,
            Vertical: null,
            Priority: priority,
            Severity: severity,
            Overdue: null,
            Unread: null,
            NowUtc: now);

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
            PrimaryActionCode: null,
            NavigationTargetCode: null,
            NavigationParameters: new Dictionary<string, string?>(),
            CreatedAtUtc: now,
            CompletedAtUtc: null,
            CancelledAtUtc: null,
            DeduplicationKey: $"valid:{Guid.NewGuid():D}",
            Version: 1,
            CorrelationId: null,
            CausationId: null,
            MetadataJson: null);

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
            PrimaryActionCode: null,
            NavigationTargetCode: null,
            NavigationParameters: new Dictionary<string, string?>(),
            CreatedAtUtc: now,
            CompletedAtUtc: null,
            CancelledAtUtc: null,
            DeduplicationKey: "invalid",
            Version: 1,
            CorrelationId: null,
            CausationId: null,
            MetadataJson: null);
}
