using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Core.WorkCenter;
using NGB.Definitions.WorkCenter;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.Runtime.WorkCenter;
using Xunit;

namespace NGB.Runtime.Tests.WorkCenter;

public sealed class WorkCenterPerformanceAndMaintenanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Recipient_resolution_batches_and_reuses_user_role_and_preference_snapshots()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        var preferences = new Mock<INotificationPreferenceRepository>(MockBehavior.Strict);
        var sales = Role("sales");

        users.Setup(repository => repository.GetByIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PlatformUser>
            {
                [firstUser] = User(firstUser),
                [secondUser] = User(secondUser)
            });
        userRoles.Setup(repository => repository.GetRolesForUsersAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<PlatformRole>>
            {
                [firstUser] = [sales],
                [secondUser] = [sales]
            });
        preferences.Setup(repository => repository.GetForUsersAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Preference(firstUser, "test.first", enabled: true),
                Preference(firstUser, "test.second", enabled: false),
                Preference(secondUser, "test.first", enabled: true),
                Preference(secondUser, "test.second", enabled: true)
            ]);

        var definitions = Registry(
            Definition("test.first", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sales" }),
            Definition("test.second", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sales" }));
        var resolver = new WorkCenterPreferenceRecipientResolver(
            preferences.Object,
            users.Object,
            roles.Object,
            userRoles.Object,
            definitions);

        var first = await resolver.ResolveAsync(
            "test.first",
            WorkCenterPreferenceKind.Notification,
            [firstUser, secondUser],
            CancellationToken.None);
        var second = await resolver.ResolveAsync(
            "test.second",
            WorkCenterPreferenceKind.Notification,
            [firstUser, secondUser],
            CancellationToken.None);

        first.Should().BeEquivalentTo([firstUser, secondUser]);
        second.Should().Equal(secondUser);
        users.VerifyAll();
        userRoles.VerifyAll();
        preferences.VerifyAll();
        roles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Role_resolution_caches_role_and_members_within_one_projection_scope()
    {
        var userId = Guid.NewGuid();
        var sales = Role("sales");
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        var preferences = new Mock<INotificationPreferenceRepository>(MockBehavior.Strict);
        users.Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PlatformUser> { [userId] = User(userId) });
        roles.Setup(repository => repository.GetByCodeAsync("sales", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sales);
        userRoles.Setup(repository => repository.GetUserIdsForRoleAsync(
                sales.RoleId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([userId]);
        preferences.Setup(repository => repository.GetForUsersAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var resolver = new WorkCenterPreferenceRecipientResolver(
            preferences.Object,
            users.Object,
            roles.Object,
            userRoles.Object,
            Registry(Definition("test.task", roles: null, WorkCenterPreferenceKind.Task)));

        await resolver.ResolveRoleAssignmentAsync(
            "test.task", WorkCenterPreferenceKind.Task, "sales", CancellationToken.None);
        await resolver.ResolveRoleAssignmentAsync(
            "test.task", WorkCenterPreferenceKind.Task, "sales", CancellationToken.None);

        roles.Verify(repository => repository.GetByCodeAsync("sales", It.IsAny<CancellationToken>()), Times.Once);
        userRoles.Verify(repository => repository.GetUserIdsForRoleAsync(
            sales.RoleId, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        users.Verify(repository => repository.GetByIdsAsync(
            It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        preferences.Verify(repository => repository.GetForUsersAsync(
            It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Role_resolution_rejects_excessive_fan_out_before_loading_user_state()
    {
        var sales = Role("sales");
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        var preferences = new Mock<INotificationPreferenceRepository>(MockBehavior.Strict);
        roles.Setup(repository => repository.GetByCodeAsync("sales", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sales);
        userRoles.Setup(repository => repository.GetUserIdsForRoleAsync(
                sales.RoleId,
                2_001,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 2_001).Select(_ => Guid.NewGuid()).ToArray());
        var resolver = new WorkCenterPreferenceRecipientResolver(
            preferences.Object,
            users.Object,
            roles.Object,
            userRoles.Object,
            Registry(Definition("test.task", roles: null, WorkCenterPreferenceKind.Task)));

        var action = () => resolver.ResolveRoleAssignmentAsync(
            "test.task", WorkCenterPreferenceKind.Task, "sales", CancellationToken.None);

        await action.Should().ThrowAsync<NGB.Tools.Exceptions.NgbConfigurationViolationException>();
        users.VerifyNoOtherCalls();
        preferences.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Maintenance_uses_configured_cutoffs_and_stops_after_an_empty_batch()
    {
        var uow = UnitOfWork();
        var repository = new Mock<IWorkCenterMaintenanceRepository>(MockBehavior.Strict);
        WorkCenterRetentionCutoffs? captured = null;
        var calls = 0;
        repository.Setup(candidate => candidate.PruneAsync(
                It.IsAny<WorkCenterRetentionCutoffs>(), 250, It.IsAny<CancellationToken>()))
            .Returns((WorkCenterRetentionCutoffs cutoffs, int _, CancellationToken _) =>
            {
                captured = cutoffs;
                calls++;
                return Task.FromResult(calls == 1
                    ? new WorkCenterPruneResult(1, 2, 3, 4, 5)
                    : new WorkCenterPruneResult(0, 0, 0, 0, 0));
            });
        var options = Options.Create(new NgbWorkCenterOptions
        {
            DocumentActionExecutionRetention = TimeSpan.FromDays(10),
            TerminalTaskRetention = TimeSpan.FromDays(20),
            NotificationDeliveryRetention = TimeSpan.FromDays(30),
            OutboxRetention = TimeSpan.FromDays(40),
            MaintenanceBatchSize = 250,
            MaximumMaintenanceBatchesPerRun = 5
        });
        var service = new WorkCenterMaintenanceService(
            uow.Object,
            repository.Object,
            new FixedTimeProvider(Now),
            options);

        (await service.PruneAsync(CancellationToken.None)).Should().Be(15);

        captured.Should().Be(new WorkCenterRetentionCutoffs(
            Now.UtcDateTime.AddDays(-10),
            Now.UtcDateTime.AddDays(-20),
            Now.UtcDateTime.AddDays(-30),
            Now.UtcDateTime.AddDays(-40)));
        repository.Verify(candidate => candidate.PruneAsync(
            It.IsAny<WorkCenterRetentionCutoffs>(), 250, It.IsAny<CancellationToken>()), Times.Exactly(2));
        uow.Verify(candidate => candidate.CommitAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void Options_validation_accepts_defaults_and_rejects_unbounded_operational_values()
    {
        var validator = new NgbWorkCenterOptionsValidator();

        validator.Validate(null, new NgbWorkCenterOptions()).Succeeded.Should().BeTrue();
        var invalid = validator.Validate(null, new NgbWorkCenterOptions
        {
            DocumentActionExecutionRetention = TimeSpan.Zero,
            TerminalTaskRetention = TimeSpan.FromDays(3651),
            MaintenanceBatchSize = 0,
            MaximumMaintenanceBatchesPerRun = 0
        });

        invalid.Failed.Should().BeTrue();
        invalid.Failures.Should().Contain(message => message.Contains(nameof(NgbWorkCenterOptions.DocumentActionExecutionRetention)));
        invalid.Failures.Should().Contain(message => message.Contains(nameof(NgbWorkCenterOptions.TerminalTaskRetention)));
        invalid.Failures.Should().Contain(message => message.Contains(nameof(NgbWorkCenterOptions.MaintenanceBatchSize)));
        invalid.Failures.Should().Contain(message => message.Contains(nameof(NgbWorkCenterOptions.MaximumMaintenanceBatchesPerRun)));
    }

    private static Mock<IUnitOfWork> UnitOfWork()
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(candidate => candidate.HasActiveTransaction).Returns(false);
        uow.Setup(candidate => candidate.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        uow.Setup(candidate => candidate.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        uow.Setup(candidate => candidate.RollbackAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return uow;
    }

    private static WorkCenterPreferenceDefinitionRegistry Registry(
        params WorkCenterPreferenceDefinition[] definitions)
        => new([new DefinitionSource(definitions)]);

    private static WorkCenterPreferenceDefinition Definition(
        string code,
        IReadOnlySet<string>? roles,
        WorkCenterPreferenceKind kind = WorkCenterPreferenceKind.Notification)
        => new(
            code,
            kind,
            code,
            "Tests",
            DefaultEnabled: true,
            UserCanDisable: true,
            NotificationSeverity.Information,
            new HashSet<NotificationChannel> { NotificationChannel.InApp },
            Retention: null,
            ApplicableRoleCodes: roles);

    private static NotificationPreferenceRecord Preference(Guid userId, string code, bool enabled)
        => new(userId, code, NotificationChannel.InApp, enabled, Now.UtcDateTime, 1);

    private static PlatformUser User(Guid userId)
        => new(userId, $"subject-{userId:N}", null, null, true, Now.UtcDateTime, Now.UtcDateTime);

    private static PlatformRole Role(string code)
        => new(Guid.NewGuid(), code, code, null, false, true, Now.UtcDateTime, Now.UtcDateTime);

    private sealed class DefinitionSource(IReadOnlyList<WorkCenterPreferenceDefinition> definitions)
        : IWorkCenterPreferenceDefinitionSource
    {
        public IReadOnlyList<WorkCenterPreferenceDefinition> GetDefinitions() => definitions;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
