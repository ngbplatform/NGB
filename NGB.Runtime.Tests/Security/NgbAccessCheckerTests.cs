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
