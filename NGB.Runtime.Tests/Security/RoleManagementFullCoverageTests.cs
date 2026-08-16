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
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class RoleManagementFullCoverageTests
{
    [Fact]
    public async Task RoleOperations_RejectNullMissingAndNullPermissionCollections()
    {
        var id = Guid.NewGuid();
        var fixture = new Fixture();
        fixture.Roles.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformRole?)null);

        await ((Func<Task>)(() => fixture.Sut.GetRoleAsync(id, default)))
            .Should().ThrowAsync<SecurityRoleNotFoundException>();
        await ((Func<Task>)(() => fixture.Sut.CreateRoleAsync(null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.UpdateRoleAsync(id, null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.UpdateRoleAsync(
                id, new("code", "name", null, true, []), default)))
            .Should().ThrowAsync<SecurityRoleNotFoundException>();
        await ((Func<Task>)(() => fixture.Sut.ReplaceRolePermissionsAsync(id, null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.ReplaceRolePermissionsAsync(id, new([]), default)))
            .Should().ThrowAsync<SecurityRoleNotFoundException>();
        await ((Func<Task>)(() => fixture.Sut.ReactivateRoleAsync(id, default)))
            .Should().ThrowAsync<SecurityRoleNotFoundException>();

        var existing = Role(id);
        fixture.Roles.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        await ((Func<Task>)(() => fixture.Sut.CreateRoleAsync(
                new("code", "name", null, null!), default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.UpdateRoleAsync(
                id, new("code", "name", null, true, null!), default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.ReplaceRolePermissionsAsync(
                id, new(null!), default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task CreateRole_CoversCompleteAuditNormalizationHumanizationAndReadback()
    {
        var fixture = new Fixture();
        Guid createdId = default;
        fixture.Roles.Setup(x => x.UpsertAsync(
                It.IsAny<Guid>(), "role", "Role", "Description", false, true, It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, string?, bool, bool, CancellationToken>((id, _, _, _, _, _, _) => createdId = id)
            .Returns<Guid, string, string, string?, bool, bool, CancellationToken>(
                (id, code, name, description, system, active, _) =>
                    Task.FromResult(new PlatformRole(id, code, name, description, system, active, DateTime.UtcNow, DateTime.UtcNow)));
        fixture.Roles.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Role(createdId, system: false, active: true));
        fixture.Permissions.Setup(x => x.GetRolePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NgbPermissionKey("x", "a_b", "v")]);

        var result = await fixture.Sut.CreateRoleAsync(
            new("role", "Role", "Description", [
                new PermissionAssignmentDto("x", "a_b", "v"),
                new PermissionAssignmentDto("X", "A_B", "V")
            ]), default);

        result.RoleId.Should().Be(createdId);
        fixture.Permissions.Verify(x => x.ReplaceRolePermissionsAsync(
            createdId,
            It.Is<IReadOnlyList<NgbPermissionKey>>(keys => keys.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.AuditPayloads.Should().ContainSingle(payload =>
            payload.Contains("A B: V", StringComparison.Ordinal)
            && payload.Contains("\"Inactive\"", StringComparison.Ordinal) == false);
    }

    [Fact]
    public async Task UpdateRole_CoversSystemActiveTransitionsPermissionsAndAffectedUsers()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var fixture = new Fixture();
        fixture.Roles.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Role(id, system: true, active: true));
        fixture.Permissions.Setup(x => x.GetRolePermissionsAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NgbPermissionKey("system", "users", "view")]);
        fixture.UserRoles.Setup(x => x.GetUserIdsForRoleAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([userId]);

        var result = await fixture.Sut.UpdateRoleAsync(
            id,
            new("updated", "Updated", null, false,
                [new PermissionAssignmentDto("system", "roles", "manage")]),
            default);

        result.IsActive.Should().BeTrue("the readback mock returns the stored fixture role");
        fixture.Roles.Verify(x => x.UpsertAsync(
            id, "updated", "Updated", null, true, false, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Versions.Verify(x => x.IncrementManyAsync(
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { userId })),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.AuditPayloads.Should().Contain(payload =>
            payload.Contains("\"Yes\"", StringComparison.Ordinal)
            && payload.Contains("\"Inactive\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReactivateRole_CoversPositiveStatusTransition()
    {
        var id = Guid.NewGuid();
        var fixture = new Fixture();
        fixture.Roles.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Role(id, active: false));
        fixture.UserRoles.Setup(x => x.GetUserIdsForRoleAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await fixture.Sut.ReactivateRoleAsync(id, default);

        fixture.Roles.Verify(x => x.SetActiveAsync(id, true, It.IsAny<CancellationToken>()), Times.Once);
        fixture.AuditPayloads.Should().Contain(payload =>
            payload.Contains("\"Inactive\"", StringComparison.Ordinal)
            && payload.Contains("\"Active\"", StringComparison.Ordinal));
    }

    private static PlatformRole Role(Guid id, bool system = false, bool active = true)
        => new(id, "role", "Role", null, system, active, DateTime.UtcNow, DateTime.UtcNow);

    private sealed class Fixture
    {
        public Fixture()
        {
            Uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Roles.Setup(x => x.UpsertAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns<Guid, string, string, string?, bool, bool, CancellationToken>(
                    (id, code, name, description, system, active, _) =>
                        Task.FromResult(new PlatformRole(id, code, name, description, system, active, DateTime.UtcNow, DateTime.UtcNow)));
            Roles.Setup(x => x.SetActiveAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Permissions.Setup(x => x.ReplaceRolePermissionsAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlyList<NgbPermissionKey>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            UserRoles.Setup(x => x.GetUserIdsForRoleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Users.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, PlatformUser>());
            Versions.Setup(x => x.IncrementManyAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Audit.Setup(x => x.WriteAsync(
                    It.IsAny<AuditEntityKind>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<AuditFieldChange>?>(),
                    It.IsAny<object?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<AuditEntityKind, Guid, string, IReadOnlyList<AuditFieldChange>?, object?, Guid?, CancellationToken>(
                    (_, _, _, changes, _, _, _) => AuditPayloads.Add(string.Join('|',
                        (changes ?? []).Select(change => $"{change.OldValueJson}>{change.NewValueJson}"))))
                .Returns(Task.CompletedTask);

            Sut = new RoleManagementService(
                Uow.Object,
                Roles.Object,
                UserRoles.Object,
                Permissions.Object,
                Versions.Object,
                Users.Object,
                Audit.Object);
        }

        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IPlatformRoleRepository> Roles { get; } = new(MockBehavior.Loose);
        public Mock<IPlatformUserRoleRepository> UserRoles { get; } = new(MockBehavior.Loose);
        public Mock<IPermissionSnapshotRepository> Permissions { get; } = new(MockBehavior.Loose);
        public Mock<IUserAccessVersionRepository> Versions { get; } = new(MockBehavior.Loose);
        public Mock<IPlatformUserRepository> Users { get; } = new(MockBehavior.Loose);
        public Mock<IAuditLogService> Audit { get; } = new(MockBehavior.Loose);
        public List<string> AuditPayloads { get; } = [];
        public RoleManagementService Sut { get; }
    }
}
