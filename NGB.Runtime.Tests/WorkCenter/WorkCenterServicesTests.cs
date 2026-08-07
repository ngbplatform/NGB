using System.Data.Common;
using System.Diagnostics;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.WorkCenter;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Core.WorkCenter;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.Runtime.Security;
using NGB.Runtime.WorkCenter;
using NGB.Tools.Exceptions;
using Xunit;
using NotificationChannel = NGB.Core.WorkCenter.NotificationChannel;
using NotificationSeverity = NGB.Core.WorkCenter.NotificationSeverity;
using WorkCenterItemKind = NGB.Core.WorkCenter.WorkCenterItemKind;
using WorkCenterPreferenceKind = NGB.Core.WorkCenter.WorkCenterPreferenceKind;
using WorkCenterPriority = NGB.Core.WorkCenter.WorkCenterPriority;
using WorkCenterTaskStatus = NGB.Core.WorkCenter.WorkCenterTaskStatus;

namespace NGB.Runtime.Tests.WorkCenter;

[Collection(Observability.TelemetrySerialCollection.Name)]
public sealed class WorkCenterServicesTests
{
    private static readonly DateTime Now = new(2026, 7, 26, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Task_service_creates_direct_and_role_tasks_and_preserves_outer_transactions()
    {
        var uow = new RecordingUnitOfWork();
        var tasks = new Mock<IWorkCenterTaskRepository>(MockBehavior.Strict);
        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        var preferences = new Mock<INotificationPreferenceRepository>(MockBehavior.Strict);
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        SetupActiveUsers(users);
        preferences.Setup(repository => repository.GetForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var roleRecipient = Guid.NewGuid();
        WorkCenterTask? captured = null;
        tasks.Setup(repository => repository.CreateAsync(
                It.IsAny<WorkCenterTask>(),
                It.IsAny<string?>(),
                It.IsAny<WorkCenterNavigationTargetRecord?>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Callback<WorkCenterTask, string?, WorkCenterNavigationTargetRecord?, IReadOnlyList<Guid>, CancellationToken>(
                (task, _, _, _, _) => captured = task)
            .ReturnsAsync((WorkCenterTask task, string? _, WorkCenterNavigationTargetRecord? _, IReadOnlyList<Guid> _, CancellationToken _) =>
                new WorkCenterTaskCreateResult(task.Id, BecameActive: true, Version: 1));
        roles.Setup(repository => repository.GetByCodeAsync("sales", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Role("sales", active: true));
        userRoles.Setup(repository => repository.GetUserIdsForRoleAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([roleRecipient]);
        var service = new WorkCenterTaskService(
            uow,
            tasks.Object,
            RecipientResolver(preferences, users, roles, userRoles),
            Changes(),
            new FixedTimeProvider(Now));
        var userId = Guid.NewGuid();

        var directId = await service.CreateAsync(TaskRequest(userId, null, "open"), CancellationToken.None);

        directId.Should().Be(captured!.Id);
        captured.AssignedUserId.Should().Be(userId);
        captured.AssignedRoleId.Should().BeNull();
        captured.CreatedAtUtc.Should().Be(Now);
        uow.BeginCount.Should().Be(1);
        uow.CommitCount.Should().Be(1);

        await uow.BeginTransactionAsync();
        await service.CreateAsync(TaskRequest(null, "sales", null), CancellationToken.None);

        captured!.AssignedUserId.Should().BeNull();
        captured.AssignedRoleId.Should().NotBeNull();
        uow.BeginCount.Should().Be(2, "the service must reuse the active outer transaction");
        uow.CommitCount.Should().Be(1);
        uow.EnsureActiveCount.Should().Be(1);
        await uow.RollbackAsync();
    }

    [Fact]
    public async Task Task_service_rejects_unknown_or_ambiguous_assignments_and_rolls_back()
    {
        var uow = new RecordingUnitOfWork();
        var tasks = new Mock<IWorkCenterTaskRepository>(MockBehavior.Strict);
        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        roles.Setup(repository => repository.GetByCodeAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformRole?)null);
        roles.Setup(repository => repository.GetByCodeAsync("inactive", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Role("inactive", active: false));
        roles.Setup(repository => repository.GetByCodeAsync("sales", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Role("sales", active: true));
        var service = new WorkCenterTaskService(
            uow,
            tasks.Object,
            RecipientResolver(roles: roles, userRoles: userRoles),
            Changes(),
            new FixedTimeProvider(Now));

        await FluentActions.Awaiting(() => service.CreateAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => service.CreateAsync(TaskRequest(null, "missing", null), CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
        await FluentActions.Awaiting(() => service.CreateAsync(TaskRequest(null, "inactive", null), CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
        await FluentActions.Awaiting(() => service.CreateAsync(TaskRequest(null, null, null), CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await FluentActions.Awaiting(() => service.CreateAsync(TaskRequest(Guid.NewGuid(), "sales", null), CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        uow.RollbackCount.Should().Be(5);
    }

    [Fact]
    public async Task Task_service_creates_only_a_task_and_never_an_assignment_notification()
    {
        typeof(WorkCenterTaskService)
            .GetConstructors(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)
            .Single()
            .GetParameters()
            .Should()
            .NotContain(parameter => parameter.ParameterType == typeof(INotificationService));

        var uow = new RecordingUnitOfWork();
        var tasks = new Mock<IWorkCenterTaskRepository>(MockBehavior.Strict);
        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        var role = Role("sales", active: true);
        var firstRecipient = Guid.NewGuid();
        var secondRecipient = Guid.NewGuid();
        var preferences = new Mock<INotificationPreferenceRepository>(MockBehavior.Strict);
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        SetupActiveUsers(users);
        tasks.SetupSequence(repository => repository.CreateAsync(
                It.IsAny<WorkCenterTask>(),
                It.IsAny<string?>(),
                It.IsAny<WorkCenterNavigationTargetRecord?>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkCenterTaskCreateResult(Guid.NewGuid(), BecameActive: true, Version: 3))
            .ReturnsAsync(new WorkCenterTaskCreateResult(Guid.NewGuid(), BecameActive: false, Version: 3));
        roles.Setup(repository => repository.GetByCodeAsync("sales", It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        userRoles.Setup(repository => repository.GetUserIdsForRoleAsync(
                role.RoleId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([firstRecipient, secondRecipient]);
        userRoles.Setup(repository => repository.GetRolesForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<PlatformRole>>
            {
                [firstRecipient] = [role],
                [secondRecipient] = [role]
            });
        preferences.Setup(repository => repository.GetForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var definitions = Registry(Definition(
            "test.task",
            defaultEnabled: true,
            kind: WorkCenterPreferenceKind.Task,
            applicableRoleCodes: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sales" }));
        var resolver = RecipientResolver(preferences, users, roles, userRoles, definitions);
        var service = new WorkCenterTaskService(
            uow,
            tasks.Object,
            resolver,
            Changes(),
            new FixedTimeProvider(Now));
        var request = TaskRequest(
            userId: null,
            roleCode: "sales",
            actionCode: null,
            taskCode: "test.task");

        await service.CreateAsync(request, CancellationToken.None);
        await service.CreateAsync(request, CancellationToken.None);

    }

    [Fact]
    public async Task Task_service_snapshots_only_enabled_recipients_and_skips_task_when_every_recipient_disabled()
    {
        var uow = new RecordingUnitOfWork();
        var tasks = new Mock<IWorkCenterTaskRepository>(MockBehavior.Strict);
        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        var preferences = new Mock<INotificationPreferenceRepository>(MockBehavior.Strict);
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        SetupActiveUsers(users);
        var role = Role("sales", active: true);
        var enabledRecipient = Guid.NewGuid();
        var disabledRecipient = Guid.NewGuid();
        IReadOnlyList<Guid>? capturedRecipients = null;
        var createdTaskId = Guid.NewGuid();
        roles.Setup(repository => repository.GetByCodeAsync("sales", It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        userRoles.Setup(repository => repository.GetUserIdsForRoleAsync(
                role.RoleId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([enabledRecipient, disabledRecipient]);
        userRoles.Setup(repository => repository.GetRolesForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<PlatformRole>>
            {
                [enabledRecipient] = [role],
                [disabledRecipient] = [role]
            });
        preferences.SetupSequence(repository => repository.GetForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NotificationPreferenceRecord(
                    enabledRecipient,
                    "test.task",
                    NotificationChannel.InApp,
                    true,
                    Now,
                    1),
                new NotificationPreferenceRecord(
                    disabledRecipient,
                    "test.task",
                    NotificationChannel.InApp,
                    false,
                    Now,
                    1)
            ])
            .ReturnsAsync([
                new NotificationPreferenceRecord(
                    enabledRecipient,
                    "test.task",
                    NotificationChannel.InApp,
                    false,
                    Now,
                    2),
                new NotificationPreferenceRecord(
                    disabledRecipient,
                    "test.task",
                    NotificationChannel.InApp,
                    false,
                    Now,
                    1)
            ]);
        tasks.Setup(repository => repository.CreateAsync(
                It.IsAny<WorkCenterTask>(),
                It.IsAny<string?>(),
                It.IsAny<WorkCenterNavigationTargetRecord?>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Callback<WorkCenterTask, string?, WorkCenterNavigationTargetRecord?, IReadOnlyList<Guid>, CancellationToken>(
                (_, _, _, recipients, _) => capturedRecipients = recipients)
            .ReturnsAsync(new WorkCenterTaskCreateResult(createdTaskId, BecameActive: true, Version: 1));
        var definitions = Registry(Definition(
            "test.task",
            defaultEnabled: true,
            kind: WorkCenterPreferenceKind.Task,
            applicableRoleCodes: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sales" }));
        var resolver = RecipientResolver(preferences, users, roles, userRoles, definitions);
        var service = new WorkCenterTaskService(
            uow,
            tasks.Object,
            resolver,
            Changes(),
            new FixedTimeProvider(Now));
        var request = TaskRequest(
            userId: null,
            roleCode: "sales",
            actionCode: null,
            taskCode: "test.task");

        (await service.CreateAsync(request, CancellationToken.None)).Should().Be(createdTaskId);
        capturedRecipients.Should().Equal(enabledRecipient);

        resolver.Reset();
        (await service.CreateAsync(
            request with { DeduplicationKey = "dedupe:all-disabled" },
            CancellationToken.None)).Should().BeNull();

        tasks.Verify(
            repository => repository.CreateAsync(
                It.IsAny<WorkCenterTask>(),
                It.IsAny<string?>(),
                It.IsAny<WorkCenterNavigationTargetRecord?>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Task_service_completes_and_cancels_by_deduplication_key()
    {
        var uow = new RecordingUnitOfWork();
        var tasks = new Mock<IWorkCenterTaskRepository>(MockBehavior.Strict);
        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        tasks.Setup(repository => repository.CompleteByDeduplicationKeyAsync(
                "task.code", "task:1", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkCenterTaskMutationResult(true, []));
        tasks.Setup(repository => repository.CancelByDeduplicationKeyAsync(
                "task.code", "task:2", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkCenterTaskMutationResult(true, []));
        var service = new WorkCenterTaskService(
            uow,
            tasks.Object,
            RecipientResolver(roles: roles, userRoles: userRoles),
            Changes(),
            new FixedTimeProvider(Now));

        await service.CompleteByDeduplicationKeyAsync("task.code", "task:1", CancellationToken.None);
        await service.CancelByDeduplicationKeyAsync("task.code", "task:2", CancellationToken.None);

        uow.CommitCount.Should().Be(2);
        tasks.VerifyAll();
    }

    [Fact]
    public async Task Notification_service_batches_recipients_and_resolves_preferences_severity_and_retention()
    {
        var uow = new RecordingUnitOfWork();
        var notifications = new Mock<INotificationRepository>(MockBehavior.Strict);
        var preferences = new Mock<INotificationPreferenceRepository>(MockBehavior.Strict);
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        SetupActiveUsers(users);
        var userEnabled = Guid.NewGuid();
        var userDisabled = Guid.NewGuid();
        WorkCenterNotification? captured = null;
        IReadOnlyList<Guid>? capturedRecipients = null;
        preferences.Setup(repository => repository.GetForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NotificationPreferenceRecord(
                    userEnabled,
                    "test.ready",
                    NotificationChannel.InApp,
                    true,
                    Now,
                    1),
                new NotificationPreferenceRecord(
                    userDisabled,
                    "test.ready",
                    NotificationChannel.InApp,
                    false,
                    Now,
                    1)
            ]);
        notifications.Setup(repository => repository.CreateAsync(
                It.IsAny<WorkCenterNotification>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Callback<WorkCenterNotification, IReadOnlyList<Guid>, CancellationToken>(
                (notification, recipients, _) =>
                {
                    captured = notification;
                    capturedRecipients = recipients;
                })
            .ReturnsAsync((WorkCenterNotification notification, IReadOnlyList<Guid> _, CancellationToken _) =>
                new WorkCenterNotificationCreateResult(notification.Id, [userEnabled]));
        var definitions = Registry(
            Definition("test.ready", defaultEnabled: false, retention: TimeSpan.FromDays(7)),
            Definition("test.required", defaultEnabled: false, mandatory: true, canDisable: false));
        var service = new NotificationService(
            uow,
            notifications.Object,
            RecipientResolver(preferences, users, userRoles: userRoles, definitions: definitions),
            definitions,
            Changes(),
            new FixedTimeProvider(Now));

        var result = await service.CreateAsync(
            NotificationRequest(
                "test.ready",
                [Guid.Empty, userEnabled, userEnabled, userDisabled],
                severity: NotificationSeverity.Warning),
            CancellationToken.None);

        result.Should().Be(captured!.Id);
        capturedRecipients.Should().Equal(userEnabled);
        captured.Severity.Should().Be(NotificationSeverity.Warning);
        captured.CreatedAtUtc.Should().Be(Now);
        captured.ExpiresAtUtc.Should().Be(Now.AddDays(7));

        preferences.Reset();
        preferences.Setup(repository => repository.GetForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        notifications.Reset();
        notifications.Setup(repository => repository.CreateAsync(
                It.IsAny<WorkCenterNotification>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkCenterNotification notification, IReadOnlyList<Guid> _, CancellationToken _) =>
                new WorkCenterNotificationCreateResult(notification.Id, [userDisabled]));

        (await service.CreateAsync(
            NotificationRequest("test.required", [userDisabled], severity: null, expiresAtUtc: Now.AddDays(1)),
            CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task Notification_service_returns_null_for_empty_or_fully_disabled_recipient_sets()
    {
        var uow = new RecordingUnitOfWork();
        var notifications = new Mock<INotificationRepository>(MockBehavior.Strict);
        var preferences = new Mock<INotificationPreferenceRepository>(MockBehavior.Strict);
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        var user = Guid.NewGuid();
        var inactiveUser = Guid.NewGuid();
        users.Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, CancellationToken _) =>
                ids
                    .Distinct()
                    .ToDictionary(
                        static id => id,
                        id => new PlatformUser(
                            id,
                            $"subject-{id:N}",
                            Email: null,
                            DisplayName: null,
                            IsActive: id != inactiveUser,
                            Now,
                            Now)));
        preferences.Setup(repository => repository.GetForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NotificationPreferenceRecord(
                    user,
                    "test.ready",
                    NotificationChannel.InApp,
                    false,
                    Now,
                    1)
            ]);
        var definitions = Registry(
            Definition("test.ready", defaultEnabled: false),
            Definition("test.active", defaultEnabled: true),
            Definition("test.task", defaultEnabled: true, kind: WorkCenterPreferenceKind.Task));
        var service = new NotificationService(
            uow,
            notifications.Object,
            RecipientResolver(preferences, users, userRoles: userRoles, definitions: definitions),
            definitions,
            Changes(),
            new FixedTimeProvider(Now));

        (await service.CreateAsync(NotificationRequest("test.ready", []), CancellationToken.None))
            .Should().BeNull();
        (await service.CreateAsync(NotificationRequest("test.ready", [Guid.Empty]), CancellationToken.None))
            .Should().BeNull();
        (await service.CreateAsync(NotificationRequest("test.ready", [user]), CancellationToken.None))
            .Should().BeNull();
        (await service.CreateAsync(NotificationRequest("test.active", [inactiveUser]), CancellationToken.None))
            .Should().BeNull();
        await FluentActions.Awaiting(() => service.CreateAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => service.CreateAsync(
                NotificationRequest("test.task", [user]),
                CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*registered as 'Task'*cannot create a notification*");

        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Notification_service_filters_recipients_by_definition_roles_before_preferences()
    {
        var uow = new RecordingUnitOfWork();
        var notifications = new Mock<INotificationRepository>(MockBehavior.Strict);
        var preferences = new Mock<INotificationPreferenceRepository>(MockBehavior.Strict);
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        SetupActiveUsers(users);
        var salesUser = Guid.NewGuid();
        var managerUser = Guid.NewGuid();
        var inactiveSalesUser = Guid.NewGuid();
        var salesRole = Role("sales", active: true);
        var managerRole = Role("manager", active: true);
        userRoles.Setup(repository => repository.GetRolesForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<PlatformRole>>
            {
                [salesUser] = [salesRole],
                [managerUser] = [managerRole],
                [inactiveSalesUser] = [Role("sales", active: false)]
            });
        preferences.Setup(repository => repository.GetForUsersAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { salesUser })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        notifications.Setup(repository => repository.CreateAsync(
                It.IsAny<WorkCenterNotification>(),
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { salesUser })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkCenterNotificationCreateResult(Guid.NewGuid(), [salesUser]));
        var definitions = Registry(Definition(
            "test.sales",
            defaultEnabled: true,
            applicableRoleCodes: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sales" }));
        var service = new NotificationService(
            uow,
            notifications.Object,
            RecipientResolver(preferences, users, userRoles: userRoles, definitions: definitions),
            definitions,
            Changes(),
            new FixedTimeProvider(Now));

        var result = await service.CreateAsync(
            NotificationRequest("test.sales", [salesUser, managerUser, inactiveSalesUser]),
            CancellationToken.None);

        result.Should().NotBeNull();
        notifications.VerifyAll();
    }

    [Fact]
    public async Task Query_service_uses_batched_visibility_roles_summary_and_stable_cursor_paging()
    {
        var harness = QueryHarness(bootstrapAdmin: false);
        var roleId = Guid.NewGuid();
        harness.UserRoles
            .Setup(repository => repository.GetRolesForUserAsync(harness.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Role("active", true, roleId), Role("inactive", false)]);
        harness.Reads.Setup(repository => repository.GetSummaryAsync(
                harness.UserId,
                It.Is<IReadOnlyList<Guid>>(roles => roles.SequenceEqual(new[] { roleId })),
                false,
                It.Is<IReadOnlyList<string>>(kinds => kinds.SequenceEqual(new[] { "document" })),
                It.Is<IReadOnlyList<string>>(codes => codes.SequenceEqual(new[] { "allowed" })),
                "pm",
                Now,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkCenterSummaryRecord(4, 3, 2, 5, 1, 9));

        var summary = await harness.Service.GetSummaryAsync("pm", CancellationToken.None);

        summary.Should().Be(new WorkCenterSummaryDto(4, 3, 2, 5, 1, 9));

        var first = Item(
            WorkCenterItemKind.Task,
            "allowed",
            sortAt: Now,
            dueAt: Now.AddMinutes(-1),
            navigationPath: "/payments/1",
            navigationCode: "document.editor");
        var second = Item(
            WorkCenterItemKind.Notification,
            "allowed",
            sortAt: Now.AddSeconds(-1),
            dueAt: null,
            navigationPath: null,
            navigationCode: null);
        var denied = Item(
            WorkCenterItemKind.Task,
            "denied",
            sortAt: Now.AddSeconds(-2),
            dueAt: null,
            navigationPath: null,
            navigationCode: "document.editor");
        WorkCenterQuery? capturedQuery = null;
        harness.Reads.Setup(repository => repository.GetItemsAsync(
                It.IsAny<WorkCenterQuery>(), It.IsAny<CancellationToken>()))
            .Callback<WorkCenterQuery, CancellationToken>((query, _) => capturedQuery = query)
            .ReturnsAsync([first, second, denied]);

        var page = await harness.Service.GetItemsAsync(
            new WorkCenterQueryDto(Limit: 1, Tab: WorkCenterTab.Attention),
            CancellationToken.None);

        capturedQuery!.Limit.Should().Be(2);
        capturedQuery.RoleIds.Should().Equal(roleId);
        page.Items.Should().ContainSingle();
        page.Items[0].IsOverdue.Should().BeTrue();
        page.Items[0].Assignment.Should().NotBeNull();
        page.Items[0].Target!.Parameters["path"].Should().Be("/payments/1");
        page.NextCursor.Should().NotBeNullOrWhiteSpace();

        await harness.Service.GetItemsAsync(
            new WorkCenterQueryDto(Cursor: page.NextCursor, Limit: 500),
            CancellationToken.None);
        capturedQuery.Cursor.Should().Be(new WorkCenterCursor(first.SortAtUtc, first.Id));
        capturedQuery.Limit.Should().Be(101);
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("bm90OmE6Y3Vyc29y")]
    public async Task Query_service_rejects_malformed_cursors(string cursor)
    {
        var harness = QueryHarness();

        await FluentActions.Awaiting(() => harness.Service.GetItemsAsync(
                new WorkCenterQueryDto(Cursor: cursor),
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task Query_service_maps_notification_fallbacks_and_filters_denied_sources_in_memory()
    {
        var harness = QueryHarness(bootstrapAdmin: true);
        harness.UserRoles
            .Setup(repository => repository.GetRolesForUserAsync(harness.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var notification = Item(
            WorkCenterItemKind.Notification,
            "anything",
            sortAt: Now,
            dueAt: null,
            navigationPath: null,
            navigationCode: null);
        harness.Reads.Setup(repository => repository.GetItemsAsync(
                It.IsAny<WorkCenterQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([notification]);

        var page = await harness.Service.GetItemsAsync(new WorkCenterQueryDto(Limit: 0), CancellationToken.None);

        page.Limit.Should().Be(1);
        page.Items[0].Assignment.Should().BeNull();
        page.Items[0].Target.Should().BeNull();
        page.Items[0].IsOverdue.Should().BeFalse();
    }

    [Fact]
    public async Task Query_service_maps_all_due_state_and_null_navigation_variants()
    {
        var harness = QueryHarness(bootstrapAdmin: true);
        var inProgress = Item(
            WorkCenterItemKind.Task,
            "anything",
            Now,
            Now.AddMinutes(-1),
            null,
            null,
            WorkCenterTaskStatus.InProgress);
        var completed = Item(
            WorkCenterItemKind.Task,
            "anything",
            Now.AddSeconds(-1),
            Now.AddMinutes(-1),
            null,
            null,
            WorkCenterTaskStatus.Completed);
        var future = Item(
            WorkCenterItemKind.Task,
            "anything",
            Now.AddSeconds(-2),
            Now.AddMinutes(1),
            null,
            null,
            WorkCenterTaskStatus.Open);
        var nullNavigation = Item(
            WorkCenterItemKind.Notification,
            "anything",
            Now.AddSeconds(-3),
            null,
            null,
            "document.editor");
        harness.Reads.Setup(repository => repository.GetItemsAsync(
                It.IsAny<WorkCenterQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([inProgress, completed, future, nullNavigation]);

        var page = await harness.Service.GetItemsAsync(
            new WorkCenterQueryDto(Limit: 10),
            CancellationToken.None);

        page.Items.Single(item => item.Id == inProgress.Id).IsOverdue.Should().BeTrue();
        page.Items.Single(item => item.Id == completed.Id).IsOverdue.Should().BeFalse();
        page.Items.Single(item => item.Id == future.Id).IsOverdue.Should().BeFalse();
        page.Items.Single(item => item.Id == nullNavigation.Id)
            .Target!.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_service_sets_activity_status_for_success_and_failure_paths()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NGB.Platform.DocumentActionsWorkCenter",
            Sample = static (ref _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        var harness = QueryHarness(bootstrapAdmin: true);
        harness.Reads.Setup(repository => repository.GetSummaryAsync(
                harness.UserId,
                It.IsAny<IReadOnlyList<Guid>>(),
                true,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(),
                null,
                Now,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkCenterSummaryRecord(1, 1, 0, 0, 0, 1));
        harness.Reads.Setup(repository => repository.GetItemsAsync(
                It.IsAny<WorkCenterQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        (await harness.Service.GetSummaryAsync(null, CancellationToken.None)).AttentionCount.Should().Be(1);
        (await harness.Service.GetItemsAsync(new WorkCenterQueryDto(), CancellationToken.None))
            .Items.Should().BeEmpty();

        await FluentActions.Awaiting(() => harness.Service.GetItemsAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        harness.Reads.Setup(repository => repository.GetSummaryAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<bool>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("read failed"));
        await FluentActions.Awaiting(() => harness.Service.GetSummaryAsync(null, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Query_service_denies_missing_inactive_or_unauthenticated_users(
        bool authenticated,
        bool active,
        bool hasUser)
    {
        var harness = QueryHarness();
        harness.Snapshots.Setup(provider => provider.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionSnapshot(
                hasUser ? harness.UserId : null,
                "subject",
                authenticated,
                active,
                false,
                1,
                []));

        await FluentActions.Awaiting(() => harness.Service.GetSummaryAsync(null, CancellationToken.None))
            .Should().ThrowAsync<NgbPermissionDeniedException>();
    }

    [Fact]
    public async Task Query_service_executes_all_mutations_atomically_and_notifies_after_commit()
    {
        var harness = QueryHarness(bootstrapAdmin: true);
        var roleId = Guid.NewGuid();
        harness.UserRoles
            .Setup(repository => repository.GetRolesForUserAsync(harness.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Role("role", true, roleId)]);
        harness.Notifications.Setup(repository => repository.MarkReadAsync(
                It.IsAny<Guid>(), harness.UserId, true,
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                Now, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        harness.Notifications.Setup(repository => repository.DismissAsync(
                It.IsAny<Guid>(), harness.UserId, true,
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                Now, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        harness.Tasks.Setup(repository => repository.MarkReadAsync(
                It.IsAny<Guid>(), harness.UserId,
                It.Is<IReadOnlyList<Guid>>(roles => roles.SequenceEqual(new[] { roleId })),
                true,
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                Now, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        harness.Tasks.Setup(repository => repository.SnoozeAsync(
                It.IsAny<Guid>(), harness.UserId,
                It.IsAny<IReadOnlyList<Guid>>(), true,
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                Now.AddDays(1), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        harness.Tasks.Setup(repository => repository.ClaimAsync(
                It.IsAny<Guid>(), harness.UserId,
                It.IsAny<IReadOnlyList<Guid>>(), true,
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                3, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        harness.Realtime.Setup(notifier => notifier.NotifyUsersChangedAsync(
                Now.Ticks,
                It.Is<IReadOnlyCollection<Guid>>(users => users.SequenceEqual(new[] { harness.UserId })),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var id = Guid.NewGuid();

        await harness.Service.MarkNotificationReadAsync(id, CancellationToken.None);
        await harness.Service.DismissNotificationAsync(id, CancellationToken.None);
        await harness.Service.MarkTaskReadAsync(id, CancellationToken.None);
        await harness.Service.SnoozeTaskAsync(id, Now.AddDays(1), CancellationToken.None);
        await harness.Service.ClaimTaskAsync(id, 3, CancellationToken.None);

        harness.Uow.CommitCount.Should().Be(5);
        harness.Realtime.Verify(
            notifier => notifier.NotifyUsersChangedAsync(
                Now.Ticks,
                It.Is<IReadOnlyCollection<Guid>>(users => users.SequenceEqual(new[] { harness.UserId })),
                It.IsAny<CancellationToken>()),
            Times.Exactly(5));

        harness.Tasks.Setup(repository => repository.ClaimAsync(
                It.IsAny<Guid>(), harness.UserId,
                It.IsAny<IReadOnlyList<Guid>>(), true,
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                4, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        await FluentActions.Awaiting(() => harness.Service.ClaimTaskAsync(id, 4, CancellationToken.None))
            .Should().ThrowAsync<WorkCenterTaskClaimConflictException>();

        await FluentActions.Awaiting(() => harness.Service.SnoozeTaskAsync(
                id,
                DateTime.SpecifyKind(Now, DateTimeKind.Local),
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task Query_service_resolves_and_updates_preferences_with_mandatory_guards()
    {
        var harness = QueryHarness();
        harness.UserRoles
            .Setup(repository => repository.GetRolesForUserAsync(harness.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        harness.Preferences.Setup(repository => repository.GetForUserAsync(
                harness.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NotificationPreferenceRecord(
                    harness.UserId,
                    "test.optional",
                    NotificationChannel.InApp,
                    false,
                    Now,
                    1),
                new NotificationPreferenceRecord(
                    harness.UserId,
                    "test.current-on",
                    NotificationChannel.InApp,
                    true,
                    Now,
                    1)
            ]);
        harness.Preferences.Setup(repository => repository.UpsertManyAsync(
                It.IsAny<IReadOnlyList<NotificationPreferenceRecord>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        harness.Realtime.Setup(notifier => notifier.NotifyUsersChangedAsync(
                Now.Ticks,
                It.Is<IReadOnlyCollection<Guid>>(users => users.SequenceEqual(new[] { harness.UserId })),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await harness.Service.GetNotificationPreferencesAsync(CancellationToken.None);

        result.Single(item => item.Code == "test.optional").IsEnabled.Should().BeFalse();
        result.Single(item => item.Code == "test.required").IsEnabled.Should().BeTrue();
        result.Single(item => item.Code == "test.current-on").IsEnabled.Should().BeTrue();
        result.Single(item => item.Code == "test.default-off").IsEnabled.Should().BeFalse();
        result.Should().NotContain(item => item.Code == "test.sales");

        await FluentActions.Awaiting(() => harness.Service.UpdateNotificationPreferencesAsync(
                new UpdateNotificationPreferencesRequestDto([
                    new UpdateNotificationPreferenceDto(
                        "test.sales",
                        NGB.Contracts.WorkCenter.NotificationChannel.InApp,
                        true)
                ]),
                CancellationToken.None))
            .Should().ThrowAsync<NgbPermissionDeniedException>();

        await harness.Service.UpdateNotificationPreferencesAsync(
            new UpdateNotificationPreferencesRequestDto([
                new UpdateNotificationPreferenceDto("test.optional", NGB.Contracts.WorkCenter.NotificationChannel.InApp, true),
                new UpdateNotificationPreferenceDto("test.required", NGB.Contracts.WorkCenter.NotificationChannel.InApp, true),
                new UpdateNotificationPreferenceDto("test.optional", NGB.Contracts.WorkCenter.NotificationChannel.InApp, false)
            ]),
            CancellationToken.None);
        harness.Preferences.Verify(repository => repository.UpsertManyAsync(
            It.Is<IReadOnlyList<NotificationPreferenceRecord>>(items =>
                items.Count == 2
                && items.Single(preference => preference.NotificationCode == "test.optional").IsEnabled == false
                && items.Single(preference => preference.NotificationCode == "test.required").IsEnabled),
            It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Preferences.Verify(repository => repository.UpsertAsync(
            It.IsAny<NotificationPreferenceRecord>(), It.IsAny<CancellationToken>()), Times.Never);

        await FluentActions.Awaiting(() => harness.Service.UpdateNotificationPreferencesAsync(
            new UpdateNotificationPreferencesRequestDto([
                new UpdateNotificationPreferenceDto("test.required", NGB.Contracts.WorkCenter.NotificationChannel.InApp, false)
                ]),
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await FluentActions.Awaiting(() => harness.Service.UpdateNotificationPreferencesAsync(
                new UpdateNotificationPreferencesRequestDto([
                    new UpdateNotificationPreferenceDto("test.locked", NGB.Contracts.WorkCenter.NotificationChannel.InApp, false)
                ]),
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await FluentActions.Awaiting(() => harness.Service.UpdateNotificationPreferencesAsync(
                new UpdateNotificationPreferencesRequestDto([
                    new UpdateNotificationPreferenceDto(
                        "test.optional",
                        (NGB.Contracts.WorkCenter.NotificationChannel)999,
                        true)
                ]),
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await FluentActions.Awaiting(() => harness.Service.UpdateNotificationPreferencesAsync(
                null!,
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();

        harness.UserRoles
            .Setup(repository => repository.GetRolesForUserAsync(
                harness.UserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Role("sales", active: true)]);
        var roleScoped = await harness.Service.GetNotificationPreferencesAsync(CancellationToken.None);
        roleScoped.Should().ContainSingle(item => item.Code == "test.sales");
    }

    private static QueryServiceHarness QueryHarness(bool bootstrapAdmin = false)
    {
        var userId = Guid.NewGuid();
        var uow = new RecordingUnitOfWork();
        var reads = new Mock<IWorkCenterReadRepository>(MockBehavior.Strict);
        var tasks = new Mock<IWorkCenterTaskRepository>(MockBehavior.Strict);
        var notifications = new Mock<INotificationRepository>(MockBehavior.Strict);
        var preferences = new Mock<INotificationPreferenceRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        var snapshots = new Mock<IPermissionSnapshotProvider>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        userRoles.Setup(repository => repository.GetRolesForUserAsync(
                userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        snapshots.Setup(provider => provider.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionSnapshot(
                userId,
                "subject",
                true,
                true,
                bootstrapAdmin,
                1,
                bootstrapAdmin
                    ? []
                    : [new NgbPermissionKey("document", "allowed", NgbPermissionActions.View)]));
        var definitions = Registry(
            Definition("test.optional", defaultEnabled: true),
            Definition("test.required", defaultEnabled: false, mandatory: true, canDisable: false),
            Definition("test.current-on", defaultEnabled: false),
            Definition("test.default-off", defaultEnabled: false),
            Definition("test.locked", defaultEnabled: true, canDisable: false),
            Definition(
                "test.sales",
                defaultEnabled: true,
                applicableRoleCodes: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sales" }));
        var service = new WorkCenterQueryService(
            uow,
            reads.Object,
            tasks.Object,
            notifications.Object,
            preferences.Object,
            userRoles.Object,
            snapshots.Object,
            definitions,
            new FixedTimeProvider(Now),
            realtime.Object);
        return new QueryServiceHarness(
            userId,
            uow,
            reads,
            tasks,
            notifications,
            preferences,
            userRoles,
            snapshots,
            realtime,
            service);
    }

    private static WorkCenterItemRecord Item(
        WorkCenterItemKind kind,
        string resourceCode,
        DateTime sortAt,
        DateTime? dueAt,
        string? navigationPath,
        string? navigationCode,
        WorkCenterTaskStatus? taskStatus = null)
        => new(
            Guid.NewGuid(),
            kind,
            kind == WorkCenterItemKind.Task ? "task.code" : "notification.code",
            "Title",
            "Description",
            "document",
            resourceCode,
            Guid.NewGuid(),
            "Source title",
            "Source subtitle",
            kind == WorkCenterItemKind.Task ? WorkCenterPriority.High : null,
            kind == WorkCenterItemKind.Notification ? NotificationSeverity.Information : null,
            kind == WorkCenterItemKind.Task ? taskStatus ?? WorkCenterTaskStatus.Open : null,
            sortAt,
            dueAt,
            IsRead: false,
            SnoozedUntilUtc: null,
            AssignedUserId: null,
            AssignedRoleId: kind == WorkCenterItemKind.Task ? Guid.NewGuid() : null,
            ClaimedByUserId: null,
            PrimaryActionCode: kind == WorkCenterItemKind.Task ? "open" : null,
            Target: navigationCode is null
                ? null
                : new WorkCenterNavigationTargetRecord(
                    navigationCode,
                    navigationPath is null
                        ? new Dictionary<string, string?>()
                        : new Dictionary<string, string?> { ["path"] = navigationPath }),
            Version: 2);

    private static CreateWorkCenterTaskRequest TaskRequest(
        Guid? userId,
        string? roleCode,
        string? actionCode,
        string taskCode = "task.code")
        => new(
            taskCode,
            new WorkCenterSourceReference("document", "document.code", Guid.NewGuid(), "Source", null),
            "Task",
            "Description",
            WorkCenterPriority.Normal,
            userId,
            roleCode,
            Now.AddDays(1),
            actionCode is null ? null : new NGB.Core.Documents.Actions.DocumentActionCode(actionCode),
            actionCode is null
                ? null
                : new DocumentActionTargetDto(
                    StandardDocumentTargets.Editor,
                    new Dictionary<string, string?>()),
            "dedupe",
            Guid.NewGuid(),
            Guid.NewGuid());

    private static CreateNotificationRequest NotificationRequest(
        string code,
        IReadOnlyList<Guid> recipients,
        NotificationSeverity? severity = null,
        DateTime? expiresAtUtc = null)
        => new(
            code,
            new WorkCenterSourceReference("document", "document.code", Guid.NewGuid(), "Source", null),
            "Notification",
            "Body",
            severity,
            recipients,
            expiresAtUtc,
            "dedupe",
            null,
            null);

    private static WorkCenterPreferenceDefinitionRegistry Registry(
        params WorkCenterPreferenceDefinition[] definitions)
        => new([new DefinitionSource(definitions)]);

    private static WorkCenterPreferenceRecipientResolver RecipientResolver(
        Mock<INotificationPreferenceRepository>? preferences = null,
        Mock<IPlatformUserRepository>? users = null,
        Mock<IPlatformRoleRepository>? roles = null,
        Mock<IPlatformUserRoleRepository>? userRoles = null,
        WorkCenterPreferenceDefinitionRegistry? definitions = null)
        => new(
            (preferences ?? new Mock<INotificationPreferenceRepository>()).Object,
            (users ?? new Mock<IPlatformUserRepository>()).Object,
            (roles ?? new Mock<IPlatformRoleRepository>()).Object,
            (userRoles ?? new Mock<IPlatformUserRoleRepository>()).Object,
            definitions ?? Registry(Definition(
                "task.code",
                defaultEnabled: true,
                kind: WorkCenterPreferenceKind.Task)));

    private static IWorkCenterChangeTracker Changes() => new WorkCenterChangeTracker();

    private static WorkCenterPreferenceDefinition Definition(
        string code,
        bool defaultEnabled,
        WorkCenterPreferenceKind kind = WorkCenterPreferenceKind.Notification,
        TimeSpan? retention = null,
        bool mandatory = false,
        bool canDisable = true,
        IReadOnlySet<string>? applicableRoleCodes = null)
        => new(
            code,
            kind,
            code,
            "Tests",
            defaultEnabled,
            canDisable,
            NotificationSeverity.Information,
            new HashSet<NotificationChannel> { NotificationChannel.InApp },
            retention,
            IsMandatory: mandatory,
            ApplicableRoleCodes: applicableRoleCodes);

    private static PlatformRole Role(string code, bool active, Guid? id = null)
        => new(
            id ?? Guid.NewGuid(),
            code,
            code,
            null,
            false,
            active,
            Now,
            Now);

    private static void SetupActiveUsers(Mock<IPlatformUserRepository> users)
        => users.Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, CancellationToken _) =>
                ids
                    .Distinct()
                    .ToDictionary(
                        static id => id,
                        static id => new PlatformUser(
                            id,
                            $"subject-{id:N}",
                            Email: null,
                            DisplayName: null,
                            IsActive: true,
                            Now,
                            Now)));

    private sealed class DefinitionSource(IReadOnlyList<WorkCenterPreferenceDefinition> definitions)
        : IWorkCenterPreferenceDefinitionSource
    {
        public IReadOnlyList<WorkCenterPreferenceDefinition> GetDefinitions() => definitions;
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public DbConnection Connection { get; } = new Mock<DbConnection>().Object;
        public DbTransaction? Transaction => null;
        public bool HasActiveTransaction { get; private set; }
        public int BeginCount { get; private set; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public int EnsureActiveCount { get; private set; }

        public Task EnsureConnectionOpenAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            BeginCount++;
            HasActiveTransaction = true;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct = default)
        {
            CommitCount++;
            HasActiveTransaction = false;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct = default)
        {
            RollbackCount++;
            HasActiveTransaction = false;
            return Task.CompletedTask;
        }

        public void EnsureActiveTransaction()
        {
            EnsureActiveCount++;
            if (!HasActiveTransaction)
                throw new InvalidOperationException("No active transaction.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record QueryServiceHarness(
        Guid UserId,
        RecordingUnitOfWork Uow,
        Mock<IWorkCenterReadRepository> Reads,
        Mock<IWorkCenterTaskRepository> Tasks,
        Mock<INotificationRepository> Notifications,
        Mock<INotificationPreferenceRepository> Preferences,
        Mock<IPlatformUserRoleRepository> UserRoles,
        Mock<IPermissionSnapshotProvider> Snapshots,
        Mock<IWorkCenterRealtimeNotifier> Realtime,
        WorkCenterQueryService Service);
}
