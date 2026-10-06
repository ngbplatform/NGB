using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Contracts.Security;
using NGB.Core.Security;
using NGB.Persistence.Security;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class EffectivePermissionServiceTests
{
    [Theory]
    [InlineData("pm-administrator")]
    [InlineData("crm.administrator")]
    public async Task GetAsync_AdministratorReceivesEveryRegisteredPermissionWithoutExplicitGrants(string roleCode)
    {
        var userId = Guid.NewGuid();
        var permissions = new Mock<IPermissionSnapshotRepository>(MockBehavior.Strict);
        var roles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        roles.Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Role(roleCode)]);
        var source = new Mock<INgbPermissionDefinitionSource>();
        source.Setup(x => x.GetDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PermissionDefinitionDto("system", "attachments", "read", "Read attachments", "Content"),
                new PermissionDefinitionDto("system", "attachments", "create", "Create attachments", "Content"),
                new PermissionDefinitionDto("system", "attachments", "delete", "Delete attachments", "Content"),
                new PermissionDefinitionDto("system", "notes", "read", "Read notes", "Content"),
                new PermissionDefinitionDto("system", "notes", "create", "Create notes", "Content"),
                new PermissionDefinitionDto("system", "notes", "update", "Update notes", "Content"),
                new PermissionDefinitionDto("system", "notes", "delete", "Delete notes", "Content"),
                new PermissionDefinitionDto("document", "future.document", "post", "Post future document", "Future")
            ]);
        using var definitions = new PermissionDefinitionRegistry([source.Object]);
        var options = new NgbAdministratorOptions();
        options.ApplicationRoleCodes.Add(roleCode.ToUpperInvariant());
        var service = new EffectivePermissionService(permissions.Object, roles.Object, definitions, Options.Create(options));

        var result = await service.GetAsync(userId, CancellationToken.None);

        result.Should().HaveCount(8);
        result.Should().Contain(NgbSystemPermissions.AttachmentsCreate);
        result.Should().Contain(NgbSystemPermissions.NotesUpdate);
        result.Should().Contain(new NgbPermissionKey("document", "future.document", "post"));
        permissions.Verify(x => x.GetEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("pm-administrator", false)]
    [InlineData("pm-reader", true)]
    public async Task GetAsync_InactiveOrUnregisteredRoleDoesNotGrantAdministratorPermissions(string roleCode, bool active)
    {
        var userId = Guid.NewGuid();
        var permissions = new Mock<IPermissionSnapshotRepository>();
        permissions.Setup(x => x.GetEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([NgbSystemPermissions.NotesRead]);
        var roles = new Mock<IPlatformUserRoleRepository>();
        roles.Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Role(roleCode) with { IsActive = active, Name = "Administrator" }]);
        using var definitions = new PermissionDefinitionRegistry([]);
        var options = new NgbAdministratorOptions();
        options.ApplicationRoleCodes.Add("pm-administrator");
        var service = new EffectivePermissionService(permissions.Object, roles.Object, definitions, Options.Create(options));

        var result = await service.GetAsync(userId, CancellationToken.None);

        result.Should().Equal(NgbSystemPermissions.NotesRead);
    }

    [Fact]
    public async Task GetAsync_NoConfiguredAdministratorRolesUsesAssignedPermissionsWithoutLoadingRoles()
    {
        var userId = Guid.NewGuid();
        var permissions = new Mock<IPermissionSnapshotRepository>();
        permissions.Setup(x => x.GetEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([NgbSystemPermissions.NotesRead]);
        var roles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        using var definitions = new PermissionDefinitionRegistry([]);
        var service = new EffectivePermissionService(
            permissions.Object, roles.Object, definitions, Options.Create(new NgbAdministratorOptions()));

        var result = await service.GetAsync(userId, CancellationToken.None);

        result.Should().Equal(NgbSystemPermissions.NotesRead);
    }

    private static PlatformRole Role(string code)
        => new(Guid.NewGuid(), code, "Administrator", null, true, true, DateTime.UtcNow, DateTime.UtcNow);
}
