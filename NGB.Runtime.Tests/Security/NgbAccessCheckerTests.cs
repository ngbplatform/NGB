using FluentAssertions;
using Moq;
using NGB.Core.Security;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class NgbAccessCheckerTests
{
    [Fact]
    public async Task GetSnapshotAsync_ReturnsCurrentSnapshot()
    {
        var snapshot = new PermissionSnapshot(
            userId: Guid.NewGuid(),
            authSubject: "user-1",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: false,
            accessVersion: 3,
            permissions: [new NgbPermissionKey("document", "pm.lease", "view")]);

        var provider = new Mock<IPermissionSnapshotProvider>();
        provider
            .Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var checker = new NgbAccessChecker(provider.Object);

        var result = await checker.GetSnapshotAsync(CancellationToken.None);

        result.Should().BeSameAs(snapshot);
    }

    [Fact]
    public async Task HasAsync_ReturnsFalse_WhenSnapshotDoesNotContainPermission()
    {
        var provider = new Mock<IPermissionSnapshotProvider>();
        provider
            .Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionSnapshot(
                userId: Guid.NewGuid(),
                authSubject: "user-1",
                isAuthenticated: true,
                isActive: true,
                isBootstrapAdmin: false,
                accessVersion: 3,
                permissions: []));

        var checker = new NgbAccessChecker(provider.Object);

        var result = await checker.HasAsync("document", "pm.lease", "view", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasAsync_ReturnsTrue_WhenSnapshotContainsPermission()
    {
        var provider = new Mock<IPermissionSnapshotProvider>();
        provider
            .Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionSnapshot(
                userId: Guid.NewGuid(),
                authSubject: "user-1",
                isAuthenticated: true,
                isActive: true,
                isBootstrapAdmin: false,
                accessVersion: 3,
                permissions: [new NgbPermissionKey("document", "pm.lease", "view")]));

        var checker = new NgbAccessChecker(provider.Object);

        var result = await checker.HasAsync("document", "pm.lease", "view", CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public void SnapshotHas_UsesCaseInsensitiveTrimmedLookup_WithoutWeakeningInvalidSegments()
    {
        var snapshot = new PermissionSnapshot(
            userId: Guid.NewGuid(),
            authSubject: "user-1",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: false,
            accessVersion: 3,
            permissions: [new NgbPermissionKey("document", "pm.lease", "view")]);

        snapshot.Has(" Document ", " PM.Lease ", " VIEW ").Should().BeTrue();
        snapshot.Has("document.owner", "pm.lease", "view").Should().BeFalse();
        snapshot.Has("document", "pm.lease", "view.owner").Should().BeFalse();
    }

    [Fact]
    public void SnapshotHasAny_UsesIndexedPermissions_AndHonorsInactiveState()
    {
        var userId = Guid.NewGuid();
        var active = new PermissionSnapshot(
            userId: userId,
            authSubject: "user-1",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: false,
            accessVersion: 3,
            permissions:
            [
                new NgbPermissionKey("catalog", "pm.property", "view"),
                new NgbPermissionKey("document", "pm.lease", "post")
            ]);
        var inactive = new PermissionSnapshot(
            userId: userId,
            authSubject: "user-1",
            isAuthenticated: true,
            isActive: false,
            isBootstrapAdmin: false,
            accessVersion: 3,
            permissions: [new NgbPermissionKey("catalog", "pm.property", "view")]);

        active.HasAny("catalog", "view").Should().BeTrue();
        active.HasAny("catalog", "manage").Should().BeFalse();
        inactive.HasAny("catalog", "view").Should().BeFalse();
    }

    [Fact]
    public void SnapshotAccessCacheKey_ChangesWithAccessVersion_WithoutLeakingAuthSubject()
    {
        var userId = Guid.NewGuid();
        var first = new PermissionSnapshot(
            userId: userId,
            authSubject: "subject-1",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: false,
            accessVersion: 3,
            permissions: [new NgbPermissionKey("catalog", "pm.property", "view")]);
        var second = new PermissionSnapshot(
            userId: userId,
            authSubject: "subject-1",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: false,
            accessVersion: 4,
            permissions: [new NgbPermissionKey("catalog", "pm.property", "view")]);

        first.AccessCacheKey.Should().NotBe(second.AccessCacheKey);
        first.AccessCacheKey.Should().Contain(userId.ToString("N"));
        first.AccessCacheKey.Should().NotContain("subject-1");
    }

    [Fact]
    public void SnapshotAccessCacheKey_SharesBootstrapAdminDefinitionsCache()
    {
        var first = new PermissionSnapshot(
            userId: Guid.NewGuid(),
            authSubject: "admin-1",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: true,
            accessVersion: 1,
            permissions: []);
        var second = new PermissionSnapshot(
            userId: Guid.NewGuid(),
            authSubject: "admin-2",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: true,
            accessVersion: 99,
            permissions: []);

        first.AccessCacheKey.Should().Be(second.AccessCacheKey);
        first.HasAny("catalog", "view").Should().BeTrue();
    }

    [Fact]
    public async Task RequireAsync_ThrowsPermissionDenied_WhenPermissionIsMissing()
    {
        var provider = new Mock<IPermissionSnapshotProvider>();
        provider
            .Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionSnapshot.Anonymous);

        var checker = new NgbAccessChecker(provider.Object);

        var act = async () => await checker.RequireAsync("document", "pm.lease", "view", CancellationToken.None);

        await act.Should().ThrowAsync<NgbPermissionDeniedException>()
            .Where(ex => ex.Permission.ToString() == "document.pm.lease.view");
    }

    [Fact]
    public async Task HasAsync_AllowsBootstrapAdmin_ForAnyPermission()
    {
        var provider = new Mock<IPermissionSnapshotProvider>();
        provider
            .Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionSnapshot(
                userId: Guid.NewGuid(),
                authSubject: "admin",
                isAuthenticated: true,
                isActive: true,
                isBootstrapAdmin: true,
                accessVersion: 1,
                permissions: []));

        var checker = new NgbAccessChecker(provider.Object);

        var result = await checker.HasAsync("system", "users", "manage", CancellationToken.None);

        result.Should().BeTrue();
    }
}
