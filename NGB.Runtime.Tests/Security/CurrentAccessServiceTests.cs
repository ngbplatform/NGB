using FluentAssertions;
using Moq;
using NGB.Core.Security;
using NGB.Persistence.Security;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class CurrentAccessServiceTests
{
    [Fact]
    public async Task GetCurrentAccessAsync_ReturnsActiveApplicationRolesFromDatabase()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var snapshot = new PermissionSnapshot(
            userId,
            "kc-user",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: false,
            accessVersion: 7,
            permissions: []);
        var activeRole = new PlatformRole(
            Guid.NewGuid(),
            "pm-test",
            "PM Test",
            null,
            IsSystem: false,
            IsActive: true,
            now,
            now);
        var inactiveRole = activeRole with
        {
            RoleId = Guid.NewGuid(),
            Code = "pm-old",
            Name = "PM Old",
            IsActive = false
        };

        var snapshots = new Mock<IPermissionSnapshotProvider>(MockBehavior.Strict);
        snapshots
            .Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles
            .Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([inactiveRole, activeRole]);

        var service = new CurrentAccessService(snapshots.Object, userRoles.Object);

        var result = await service.GetCurrentAccessAsync(CancellationToken.None);

        result.Roles.Should().ContainSingle();
        result.Roles[0].Code.Should().Be("pm-test");

        snapshots.VerifyAll();
        userRoles.VerifyAll();
    }
}
