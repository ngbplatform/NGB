using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Contracts.Security;
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
        snapshot.Has(NgbSystemPermissions.AttachmentsCreate).Should().BeTrue();
        snapshot.Has(NgbSystemPermissions.NotesUpdate).Should().BeTrue();
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

    [Fact]
    public async Task RefreshCurrentAsync_ReloadsAccessVersionWithinTheSameRequestScope()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var userId = Guid.NewGuid();
        var permissions = new Mock<IPermissionSnapshotRepository>();
        permissions
            .SetupSequence(x => x.GetUserAccessStateByAuthSubjectAsync("kc-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessState(
                userId, "kc-user", "user@example.test", "User", IsActive: true, AccessVersion: 7))
            .ReturnsAsync(new PlatformUserAccessState(
                userId, "kc-user", "user@example.test", "User", IsActive: true, AccessVersion: 8));
        permissions
            .SetupSequence(x => x.GetEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NgbPermissionKey("document", "pm.lease", "view")])
            .ReturnsAsync([new NgbPermissionKey("document", "pm.lease", "post")]);
        var provider = CreateProvider(
            new ActorIdentity("kc-user", "user@example.test", "User"),
            cache,
            permissions);

        var first = await provider.GetCurrentAsync(CancellationToken.None);
        var refreshed = await provider.RefreshCurrentAsync(CancellationToken.None);

        first.Has("document", "pm.lease", "view").Should().BeTrue();
        refreshed.Has("document", "pm.lease", "view").Should().BeFalse();
        refreshed.Has("document", "pm.lease", "post").Should().BeTrue();
        refreshed.AccessVersion.Should().Be(8);
    }

    [Fact]
    public async Task GetCurrentAsync_KeycloakAdministratorChangesDoNotReuseCachedPrivilegeState()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var userId = Guid.NewGuid();
        var permissions = new Mock<IPermissionSnapshotRepository>();
        permissions.Setup(x => x.GetUserAccessStateByAuthSubjectAsync("subject", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessState(userId, "subject", null, "User", true, 1));
        permissions.Setup(x => x.GetEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([NgbSystemPermissions.NotesRead]);
        var user = new ActorIdentity("subject", null, "User");
        var administrator = user with { AuthRoles = new HashSet<string> { "ngb-admin" } };

        var before = await CreateProvider(user, cache, permissions).GetCurrentAsync(CancellationToken.None);
        var elevated = await CreateProvider(administrator, cache, permissions).GetCurrentAsync(CancellationToken.None);
        var after = await CreateProvider(user, cache, permissions).GetCurrentAsync(CancellationToken.None);

        before.Has(NgbSystemPermissions.AttachmentsCreate).Should().BeFalse();
        elevated.Has(NgbSystemPermissions.AttachmentsCreate).Should().BeTrue();
        after.Has(NgbSystemPermissions.AttachmentsCreate).Should().BeFalse();
        after.Has(NgbSystemPermissions.NotesRead).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetCurrentAsync_DatabaseAdministratorUsesRegisteredPermissionsAndHonorsAccountStatus(bool active)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var userId = Guid.NewGuid();
        var permissions = new Mock<IPermissionSnapshotRepository>(MockBehavior.Strict);
        permissions.Setup(x => x.GetUserAccessStateByAuthSubjectAsync("subject", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessState(userId, "subject", null, "Administrator", active, 1));
        var roles = new Mock<IPlatformUserRoleRepository>();
        roles.Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlatformRole(
                Guid.NewGuid(), "pm-administrator", "Administrator", null, true, true, DateTime.UtcNow, DateTime.UtcNow)]);
        var source = new Mock<INgbPermissionDefinitionSource>();
        source.Setup(x => x.GetDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PermissionDefinitionDto("system", "attachments", "create", "Create attachments", "Content"),
                new PermissionDefinitionDto("system", "notes", "update", "Update notes", "Content")
            ]);
        using var definitions = new PermissionDefinitionRegistry([source.Object]);
        var options = new NgbAdministratorOptions();
        options.ApplicationRoleCodes.Add("pm-administrator");
        var effective = new EffectivePermissionService(permissions.Object, roles.Object, definitions, Options.Create(options));
        var provider = new PermissionSnapshotProvider(
            new TestCurrentActorContext(new ActorIdentity("subject", null, "Administrator")),
            permissions.Object,
            CreateSecurityCache(cache),
            effective);

        var snapshot = await provider.GetCurrentAsync(CancellationToken.None);

        snapshot.IsBootstrapAdmin.Should().BeFalse();
        snapshot.Has(NgbSystemPermissions.AttachmentsCreate).Should().Be(active);
        snapshot.Has(NgbSystemPermissions.NotesUpdate).Should().Be(active);
    }

    [Fact]
    public async Task GetCurrentAsync_CachedNullPermissionsFailClosedWithoutGrantingAdministratorAccess()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var userId = Guid.NewGuid();
        cache.Set<IReadOnlyList<NgbPermissionKey>?>($"ngb:security:snapshot:{userId:N}:7", null);
        var permissions = new Mock<IPermissionSnapshotRepository>(MockBehavior.Strict);
        permissions.Setup(x => x.GetUserAccessStateByAuthSubjectAsync("subject", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessState(userId, "subject", null, "User", true, 7));
        var provider = CreateProvider(new ActorIdentity("subject", null, "User"), cache, permissions);

        var snapshot = await provider.GetCurrentAsync(CancellationToken.None);

        snapshot.IsAuthenticated.Should().BeTrue();
        snapshot.IsActive.Should().BeTrue();
        snapshot.IsBootstrapAdmin.Should().BeFalse();
        snapshot.Permissions.Should().BeEmpty();
        snapshot.Has(NgbSystemPermissions.AttachmentsRead).Should().BeFalse();
        snapshot.Has(NgbSystemPermissions.NotesUpdate).Should().BeFalse();
        permissions.Verify(x => x.GetEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
