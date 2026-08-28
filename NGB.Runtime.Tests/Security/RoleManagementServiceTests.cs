using FluentAssertions;
using Moq;
using NGB.Contracts.Security;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class RoleManagementServiceTests
{
    [Fact]
    public async Task GetRolesAsync_UsesSingleBoundedRoleListQueryWithAssignedCounts()
    {
        var firstRoleId = Guid.NewGuid();
        var secondRoleId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        roles
            .Setup(x => x.GetListAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PlatformRoleListRecord(
                    new PlatformRole(firstRoleId, "pm-ap-clerk", "PM AP Clerk", null, IsSystem: true, IsActive: true, now, now),
                    AssignedUserCount: 2),
                new PlatformRoleListRecord(
                    new PlatformRole(secondRoleId, "pm-test", "PM Test", "Test access", IsSystem: false, IsActive: false, now, now),
                    AssignedUserCount: 0)
            ]);

        var service = CreateService(roles.Object);

        var result = await service.GetRolesAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result.Single(x => x.RoleId == firstRoleId).AssignedUsersCount.Should().Be(2);
        result.Single(x => x.RoleId == secondRoleId).AssignedUsersCount.Should().Be(0);
        result.Single(x => x.RoleId == secondRoleId).IsActive.Should().BeFalse();

        roles.VerifyAll();
    }

    [Fact]
    public async Task GetRoleAsync_ReturnsPermissionsAndAssignedUsersSortedByUserDisplay()
    {
        var roleId = Guid.NewGuid();
        var zedUserId = Guid.NewGuid();
        var alphaUserId = Guid.NewGuid();
        var emailOnlyUserId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var role = new PlatformRole(roleId, "pm-auditor", "PM Auditor", "Read-only audit access.", IsSystem: true, IsActive: true, now, now);
        var zed = new PlatformUser(zedUserId, "kc-zed", "zed@example.com", "Zed User", IsActive: true, now, now);
        var alpha = new PlatformUser(alphaUserId, "kc-alpha", "alpha@example.com", "Alpha User", IsActive: true, now, now);
        var emailOnly = new PlatformUser(emailOnlyUserId, "kc-email", "beta@example.com", null, IsActive: false, now, now);

        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        roles
            .Setup(x => x.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        var permissions = new Mock<IPermissionSnapshotRepository>(MockBehavior.Strict);
        permissions
            .Setup(x => x.GetRolePermissionsAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NgbPermissionKey("report", "pm.occupancy.summary", "execute"),
                new NgbPermissionKey("system", "audit", "view")
            ]);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles
            .Setup(x => x.GetUserIdsForRoleAsync(roleId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([zedUserId, alphaUserId, emailOnlyUserId]);

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users
            .Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { zedUserId, alphaUserId, emailOnlyUserId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PlatformUser>
            {
                [zedUserId] = zed,
                [alphaUserId] = alpha,
                [emailOnlyUserId] = emailOnly
            });

        var service = CreateService(
            roles.Object,
            userRoles: userRoles.Object,
            permissions: permissions.Object,
            users: users.Object);

        var result = await service.GetRoleAsync(roleId, CancellationToken.None);

        result.RoleId.Should().Be(roleId);
        result.Permissions.Should().ContainEquivalentOf(new PermissionAssignmentDto("report", "pm.occupancy.summary", "execute"));
        result.Permissions.Should().ContainEquivalentOf(new PermissionAssignmentDto("system", "audit", "view"));
        result.AssignedUsers.Select(x => x.UserId).Should().Equal(alphaUserId, emailOnlyUserId, zedUserId);
        result.AssignedUsers.Single(x => x.UserId == emailOnlyUserId).IsActive.Should().BeFalse();

        roles.VerifyAll();
        permissions.VerifyAll();
        userRoles.VerifyAll();
        users.VerifyAll();
    }

    [Fact]
    public async Task ReplaceRolePermissionsAsync_DeduplicatesPermissions_IncrementsAssignedUsers_AndWritesAudit()
    {
        var roleId = Guid.NewGuid();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var oldPermission = new NgbPermissionKey("report", "accounting.balance_sheet", "view");
        var executePermission = new NgbPermissionKey("report", "accounting.balance_sheet", "execute");
        var exportPermission = new NgbPermissionKey("report", "accounting.balance_sheet", "export");

        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        roles
            .Setup(x => x.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformRole(roleId, "pm-test", "PM Test", null, IsSystem: false, IsActive: true, now, now));

        var permissions = new Mock<IPermissionSnapshotRepository>(MockBehavior.Strict);
        permissions
            .Setup(x => x.GetRolePermissionsAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([oldPermission]);
        permissions
            .Setup(x => x.ReplaceRolePermissionsAsync(
                roleId,
                It.Is<IReadOnlyList<NgbPermissionKey>>(keys =>
                    keys.Count == 2
                    && keys.Contains(executePermission)
                    && keys.Contains(exportPermission)),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);

        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions
            .Setup(x => x.IncrementForRoleAsync(
                roleId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var auditCalls = new List<AuditCall>();
        var audit = CreateAudit(auditCalls);

        var service = CreateService(
            roles.Object,
            userRoles: userRoles.Object,
            permissions: permissions.Object,
            versions: versions.Object,
            audit: audit.Object);

        await service.ReplaceRolePermissionsAsync(
            roleId,
            new ReplaceRolePermissionsRequestDto([
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "execute"),
                new PermissionAssignmentDto("REPORT", "accounting.balance_sheet", "EXECUTE"),
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "export")
            ]),
            CancellationToken.None);

        var auditCall = auditCalls.Should().ContainSingle().Subject;
        auditCall.EntityKind.Should().Be(AuditEntityKind.SecurityRole);
        auditCall.EntityId.Should().Be(roleId);
        auditCall.ActionCode.Should().Be(AuditActionCodes.SecurityRolePermissionsReplace);
        auditCall.Changes.Should().ContainSingle(x => x.FieldPath == NgbPermissionResources.Permissions);
        auditCall.Changes[0].OldValueJson.Should().Contain("Balance Sheet: View");
        auditCall.Changes[0].NewValueJson.Should().Contain("Balance Sheet: Execute");
        auditCall.Changes[0].NewValueJson.Should().Contain("Balance Sheet: Export");

        roles.VerifyAll();
        permissions.VerifyAll();
        userRoles.VerifyAll();
        versions.VerifyAll();
        audit.VerifyAll();
    }

    [Fact]
    public async Task DeactivateRoleAsync_IncrementsAssignedUsersAndWritesStatusAudit()
    {
        var roleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        roles
            .Setup(x => x.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformRole(roleId, "pm-test", "PM Test", null, IsSystem: false, IsActive: true, now, now));
        roles
            .Setup(x => x.SetActiveAsync(roleId, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);

        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions
            .Setup(x => x.IncrementForRoleAsync(
                roleId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var auditCalls = new List<AuditCall>();
        var audit = CreateAudit(auditCalls);

        var service = CreateService(
            roles.Object,
            userRoles: userRoles.Object,
            versions: versions.Object,
            audit: audit.Object);

        await service.DeactivateRoleAsync(roleId, CancellationToken.None);

        var auditCall = auditCalls.Should().ContainSingle().Subject;
        auditCall.EntityKind.Should().Be(AuditEntityKind.SecurityRole);
        auditCall.EntityId.Should().Be(roleId);
        auditCall.ActionCode.Should().Be(AuditActionCodes.SecurityRoleDeactivate);
        auditCall.Changes.Should().ContainSingle(x => x.FieldPath == "status");
        auditCall.Changes[0].OldValueJson.Should().Be("\"Active\"");
        auditCall.Changes[0].NewValueJson.Should().Be("\"Inactive\"");

        roles.VerifyAll();
        userRoles.VerifyAll();
        versions.VerifyAll();
        audit.VerifyAll();
    }

    private static RoleManagementService CreateService(
        IPlatformRoleRepository roles,
        IPlatformUserRoleRepository? userRoles = null,
        IPermissionSnapshotRepository? permissions = null,
        IUserAccessVersionRepository? versions = null,
        IPlatformUserRepository? users = null,
        IAuditLogService? audit = null)
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var defaultAudit = CreateAudit([]);

        return new RoleManagementService(
            uow.Object,
            roles,
            userRoles ?? new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict).Object,
            permissions ?? new Mock<IPermissionSnapshotRepository>(MockBehavior.Strict).Object,
            versions ?? new Mock<IUserAccessVersionRepository>(MockBehavior.Strict).Object,
            users ?? new Mock<IPlatformUserRepository>(MockBehavior.Strict).Object,
            audit ?? defaultAudit.Object);
    }

    private static Mock<IAuditLogService> CreateAudit(List<AuditCall> calls)
    {
        var audit = new Mock<IAuditLogService>(MockBehavior.Strict);
        audit
            .Setup(x => x.WriteAsync(
                It.IsAny<AuditEntityKind>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AuditFieldChange>?>(),
                It.IsAny<object?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<AuditEntityKind, Guid, string, IReadOnlyList<AuditFieldChange>?, object?, Guid?, CancellationToken>((kind, entityId, actionCode, changes, _, _, _) =>
                calls.Add(new AuditCall(kind, entityId, actionCode, changes ?? Array.Empty<AuditFieldChange>())))
            .Returns(Task.CompletedTask);

        return audit;
    }

    private sealed record AuditCall(
        AuditEntityKind EntityKind,
        Guid EntityId,
        string ActionCode,
        IReadOnlyList<AuditFieldChange> Changes);
}
