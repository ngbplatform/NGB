using FluentAssertions;
using Microsoft.Extensions.Options;
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

public sealed class AdministratorRoleProtectionTests
{
    [Theory]
    [InlineData("pm-administrator")]
    [InlineData("crm.administrator")]
    public async Task Details_ReturnFullAccessIncludingNewPermissionsWithoutReadingStoredGrants(string code)
    {
        using var fixture = new Fixture(code);

        var result = await fixture.Service.GetRoleAsync(fixture.Role.RoleId, default);

        result.HasFullAccess.Should().BeTrue();
        result.Permissions.Should().BeEquivalentTo(fixture.AllPermissions);
        fixture.Permissions.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("pm-administrator")]
    [InlineData("crm.administrator")]
    public async Task ProtectedMutations_AreRejectedBeforeAnyWrites(string code)
    {
        using var fixture = new Fixture(code, system: false);
        var id = fixture.Role.RoleId;
        var requests = new UpdateRoleRequestDto[]
        {
            new("ordinary", "Changed", null, true, null),
            new(code, "Changed", null, false, null),
            new(code, "Changed", null, true, []),
            new(code, "Changed", null, true, [fixture.AllPermissions[0]])
        };

        foreach (var request in requests)
        {
            var error = await Assert.ThrowsAsync<SecurityAdministratorRoleProtectedException>(() =>
                fixture.Service.UpdateRoleAsync(id, request, default));
            error.ErrorCode.Should().Be(SecurityAdministratorRoleProtectedException.Code);
        }

        await Assert.ThrowsAsync<SecurityAdministratorRoleProtectedException>(() =>
            fixture.Service.DeactivateRoleAsync(id, default));
        await Assert.ThrowsAsync<SecurityAdministratorRoleProtectedException>(() =>
            fixture.Service.ReplaceRolePermissionsAsync(id, new(fixture.AllPermissions), default));

        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Update_RejectsMissingCodeBeforeAnyWrites(string? code)
    {
        using var fixture = new Fixture("pm-administrator");

        await Assert.ThrowsAsync<NgbArgumentRequiredException>(() =>
            fixture.Service.UpdateRoleAsync(fixture.Role.RoleId, new(code!, "Changed", null, true, null), default));

        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData("pm-administrator")]
    [InlineData(" PM-ADMINISTRATOR ")]
    [InlineData("crm.administrator")]
    [InlineData(" CRM.ADMINISTRATOR ")]
    public async Task RegisteredCodes_CannotBeClaimedByCreatingOrRenamingAnOrdinaryRole(string code)
    {
        using var fixture = new Fixture("ordinary");

        await Assert.ThrowsAsync<SecurityAdministratorRoleProtectedException>(() =>
            fixture.Service.CreateRoleAsync(new(code, "Administrator", null, []), default));
        await Assert.ThrowsAsync<SecurityAdministratorRoleProtectedException>(() =>
            fixture.Service.UpdateRoleAsync(fixture.Role.RoleId, new(code, "Administrator", null, true, []), default));

        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfileUpdate_PreservesStoredGrantsAndReturnsFullAccess(bool includeUnchangedPermissions)
    {
        using var fixture = new Fixture("pm-administrator");
        var result = await fixture.Service.UpdateRoleAsync(
            fixture.Role.RoleId,
            new("pm-administrator", "Platform administrators", "Updated description", true,
                includeUnchangedPermissions ? fixture.AllPermissions : null),
            default);

        result.Name.Should().Be("Platform administrators");
        result.Description.Should().Be("Updated description");
        result.HasFullAccess.Should().BeTrue();
        result.Permissions.Should().BeEquivalentTo(fixture.AllPermissions);
        fixture.Permissions.Verify(x => x.ReplaceRolePermissionsAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<NgbPermissionKey>>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Versions.Verify(x => x.IncrementForRoleAsync(fixture.Role.RoleId, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.SecurityRole, fixture.Role.RoleId, AuditActionCodes.SecurityRoleUpdate,
            It.IsAny<IReadOnlyList<AuditFieldChange>?>(), It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrdinarySystemRole_WithAdministratorDisplayNameRemainsEditable()
    {
        using var fixture = new Fixture("ordinary");
        var result = await fixture.Service.UpdateRoleAsync(
            fixture.Role.RoleId, new("renamed", "Administrator", null, false, []), default);

        result.HasFullAccess.Should().BeFalse();
        result.Code.Should().Be("renamed");
        result.IsActive.Should().BeFalse();
        fixture.Permissions.Verify(x => x.ReplaceRolePermissionsAsync(
            fixture.Role.RoleId, It.IsAny<IReadOnlyList<NgbPermissionKey>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreviouslyInactiveAdministrator_CanBeReactivated()
    {
        using var fixture = new Fixture("crm.administrator", active: false);

        await fixture.Service.ReactivateRoleAsync(fixture.Role.RoleId, default);

        fixture.Roles.Verify(x => x.SetActiveAsync(fixture.Role.RoleId, true, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Administrator")]
    public void UnregisteredCodes_DoNotHaveAdministratorSemantics(string? code)
    {
        var options = new NgbAdministratorOptions();
        options.ApplicationRoleCodes.Add("pm-administrator");

        options.IsAdministratorRoleCode(code).Should().BeFalse();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PermissionDefinitionRegistry _definitions;

        public Fixture(string code, bool active = true, bool system = true)
        {
            Role = new PlatformRole(
                Guid.NewGuid(), code, "Administrator", null, system, active, DateTime.UtcNow, DateTime.UtcNow);
            Roles.Setup(x => x.GetByIdAsync(Role.RoleId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Role);
            Roles.Setup(x => x.UpsertAsync(
                    Role.RoleId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), true,
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns<Guid, string, string, string?, bool, bool, CancellationToken>((_, nextCode, name, description, _, isActive, _) =>
                {
                    Role = Role with { Code = nextCode, Name = name, Description = description, IsActive = isActive };
                    return Task.FromResult(Role);
                });
            Permissions.Setup(x => x.GetRolePermissionsAsync(Role.RoleId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            var userRoles = new Mock<IPlatformUserRoleRepository>();
            userRoles.Setup(x => x.GetUserIdsForRoleAsync(Role.RoleId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            var users = new Mock<IPlatformUserRepository>();
            users.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, PlatformUser>());
            var source = new Mock<INgbPermissionDefinitionSource>();
            source.Setup(x => x.GetDefinitionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(AllPermissions.Select(permission => new PermissionDefinitionDto(
                    permission.ResourceKind, permission.ResourceCode, permission.ActionCode, "Permission", "Test")).ToArray());
            _definitions = new PermissionDefinitionRegistry([source.Object]);
            var options = new NgbAdministratorOptions();
            options.ApplicationRoleCodes.UnionWith(["pm-administrator", "crm.administrator"]);
            Service = new RoleManagementService(
                Uow.Object, Roles.Object, userRoles.Object, Permissions.Object, Versions.Object,
                users.Object, Audit.Object, Options.Create(options), _definitions);
        }

        public PlatformRole Role { get; private set; }
        public PermissionAssignmentDto[] AllPermissions { get; } =
        [
            new("system", "attachments", "read"),
            new("document", "future.document", "post")
        ];
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IPlatformRoleRepository> Roles { get; } = new();
        public Mock<IPermissionSnapshotRepository> Permissions { get; } = new();
        public Mock<IUserAccessVersionRepository> Versions { get; } = new();
        public Mock<IAuditLogService> Audit { get; } = new();
        public RoleManagementService Service { get; }

        public void AssertNoWrites()
        {
            Roles.Verify(x => x.UpsertAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            Roles.Verify(x => x.SetActiveAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            Permissions.VerifyNoOtherCalls();
            Versions.VerifyNoOtherCalls();
            Audit.VerifyNoOtherCalls();
            Uow.VerifyNoOtherCalls();
        }

        public void Dispose() => _definitions.Dispose();
    }
}
