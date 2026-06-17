using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Runtime.CurrentActor;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class PermissionSnapshotProviderTests
{
    [Fact]
    public async Task GetCurrentAsync_DeniesAnonymousActor()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = CreateProvider(currentActor: null, cache: cache);

        var snapshot = await provider.GetCurrentAsync(CancellationToken.None);

        snapshot.IsAuthenticated.Should().BeFalse();
        snapshot.Has(new NgbPermissionKey("system", "users", "view")).Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentAsync_AllowsBootstrapAdmin_WhenPlatformUserDoesNotExist()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var users = new Mock<IPlatformUserRepository>();
        users
            .Setup(x => x.GetByAuthSubjectAsync("admin-subject", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);

        var provider = CreateProvider(
            new ActorIdentity("admin-subject", "admin@example.test", "Admin", AuthRoles: new HashSet<string> { "ngb-admin" }),
            cache,
            users);

        var snapshot = await provider.GetCurrentAsync(CancellationToken.None);

        snapshot.IsBootstrapAdmin.Should().BeTrue();
        snapshot.IsActive.Should().BeTrue();
        snapshot.Has(new NgbPermissionKey("system", "roles", "manage")).Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentAsync_CachesPermissionsByUserAndAccessVersion()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var userId = Guid.NewGuid();
        var user = new PlatformUser(
            userId,
            "kc-user",
            "user@example.test",
            "User",
            IsActive: true,
            DateTime.UtcNow,
            DateTime.UtcNow);

        var users = new Mock<IPlatformUserRepository>();
        users
            .Setup(x => x.GetByAuthSubjectAsync("kc-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var versions = new Mock<IUserAccessVersionRepository>();
        versions
            .SetupSequence(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 7, DateTime.UtcNow))
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 8, DateTime.UtcNow));

        var permissions = new Mock<IPermissionSnapshotRepository>();
        permissions
            .SetupSequence(x => x.GetEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NgbPermissionKey("document", "pm.lease", "view")])
            .ReturnsAsync([new NgbPermissionKey("document", "pm.lease", "post")]);

        var provider = CreateProvider(
            new ActorIdentity("kc-user", "user@example.test", "User"),
            cache,
            users,
            versions,
            permissions);

        var first = await provider.GetCurrentAsync(CancellationToken.None);
        var second = await provider.GetCurrentAsync(CancellationToken.None);
        var nextRequestProvider = CreateProvider(
            new ActorIdentity("kc-user", "user@example.test", "User"),
            cache,
            users,
            versions,
            permissions);
        var third = await nextRequestProvider.GetCurrentAsync(CancellationToken.None);

        first.Has(new NgbPermissionKey("document", "pm.lease", "view")).Should().BeTrue();
        second.Should().BeSameAs(first);
        second.Has(new NgbPermissionKey("document", "pm.lease", "view")).Should().BeTrue();
        third.Has(new NgbPermissionKey("document", "pm.lease", "post")).Should().BeTrue();
        users.Verify(x => x.GetByAuthSubjectAsync("kc-user", It.IsAny<CancellationToken>()), Times.Exactly(2));
        versions.Verify(x => x.GetAsync(userId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        permissions.Verify(x => x.GetEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static PermissionSnapshotProvider CreateProvider(
        ActorIdentity? currentActor,
        IMemoryCache cache,
        Mock<IPlatformUserRepository>? users = null,
        Mock<IUserAccessVersionRepository>? versions = null,
        Mock<IPermissionSnapshotRepository>? permissions = null)
    {
        return new PermissionSnapshotProvider(
            new TestCurrentActorContext(currentActor),
            (users ?? new Mock<IPlatformUserRepository>()).Object,
            (versions ?? new Mock<IUserAccessVersionRepository>()).Object,
            (permissions ?? new Mock<IPermissionSnapshotRepository>()).Object,
            cache);
    }

    private sealed class TestCurrentActorContext(ActorIdentity? current) : ICurrentActorContext
    {
        public ActorIdentity? Current { get; } = current;
    }
}
