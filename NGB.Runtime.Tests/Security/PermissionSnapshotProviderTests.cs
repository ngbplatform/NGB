using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Core.Security;
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
        var permissions = new Mock<IPermissionSnapshotRepository>();
        permissions
            .Setup(x => x.GetUserAccessStateByAuthSubjectAsync("admin-subject", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserAccessState?)null);

        var provider = CreateProvider(
            new ActorIdentity("admin-subject", "admin@example.test", "Admin", AuthRoles: new HashSet<string> { "ngb-admin" }),
            cache,
            permissions);

        var snapshot = await provider.GetCurrentAsync(CancellationToken.None);

        snapshot.IsBootstrapAdmin.Should().BeTrue();
        snapshot.IsActive.Should().BeTrue();
        snapshot.Has(new NgbPermissionKey("system", "roles", "manage")).Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentAsync_DoesNotLoadEffectivePermissions_ForBootstrapAdminPlatformUser()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var userId = Guid.NewGuid();
        var permissions = new Mock<IPermissionSnapshotRepository>(MockBehavior.Strict);
        permissions
            .Setup(x => x.GetUserAccessStateByAuthSubjectAsync("admin-subject", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessState(
                userId,
                "admin-subject",
                "admin@example.test",
                "Admin",
                IsActive: true,
                AccessVersion: 7));

        var provider = CreateProvider(
            new ActorIdentity("admin-subject", "admin@example.test", "Admin", AuthRoles: new HashSet<string> { "ngb-admin" }),
            cache,
            permissions);

        var snapshot = await provider.GetCurrentAsync(CancellationToken.None);

        snapshot.IsBootstrapAdmin.Should().BeTrue();
        snapshot.Has(new NgbPermissionKey("system", "roles", "manage")).Should().BeTrue();
        permissions.Verify(x => x.GetEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentAsync_CachesPermissionsByUserAndAccessVersion()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var userId = Guid.NewGuid();
        var accessState = new PlatformUserAccessState(
            userId,
            "kc-user",
            "user@example.test",
            "User",
            IsActive: true,
            AccessVersion: 7);

        var permissions = new Mock<IPermissionSnapshotRepository>();
        permissions
            .SetupSequence(x => x.GetUserAccessStateByAuthSubjectAsync("kc-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessState)
            .ReturnsAsync(accessState with { AccessVersion = 8 });
        permissions
            .SetupSequence(x => x.GetEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NgbPermissionKey("document", "pm.lease", "view")])
            .ReturnsAsync([new NgbPermissionKey("document", "pm.lease", "post")]);

        var provider = CreateProvider(
            new ActorIdentity("kc-user", "user@example.test", "User"),
            cache,
            permissions);

        var first = await provider.GetCurrentAsync(CancellationToken.None);
        var second = await provider.GetCurrentAsync(CancellationToken.None);
        var nextRequestProvider = CreateProvider(
            new ActorIdentity("kc-user", "user@example.test", "User"),
            cache,
            permissions);
        var third = await nextRequestProvider.GetCurrentAsync(CancellationToken.None);

        first.Has(new NgbPermissionKey("document", "pm.lease", "view")).Should().BeTrue();
        second.Should().BeSameAs(first);
        second.Has(new NgbPermissionKey("document", "pm.lease", "view")).Should().BeTrue();
        third.Has(new NgbPermissionKey("document", "pm.lease", "post")).Should().BeTrue();
        permissions.Verify(x => x.GetUserAccessStateByAuthSubjectAsync("kc-user", It.IsAny<CancellationToken>()), Times.Exactly(2));
        permissions.Verify(x => x.GetEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static PermissionSnapshotProvider CreateProvider(
        ActorIdentity? currentActor,
        IMemoryCache cache,
        Mock<IPermissionSnapshotRepository>? permissions = null)
    {
        return new PermissionSnapshotProvider(
            new TestCurrentActorContext(currentActor),
            (permissions ?? new Mock<IPermissionSnapshotRepository>()).Object,
            CreateSecurityCache(cache));
    }

    private static NgbSecurityCache CreateSecurityCache(IMemoryCache cache)
        => new(cache, new TestOptionsMonitor<NgbSecurityCacheOptions>(new NgbSecurityCacheOptions()));

    private sealed class TestCurrentActorContext(ActorIdentity? current) : ICurrentActorContext
    {
        public ActorIdentity? Current { get; } = current;
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
