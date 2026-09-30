using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Contracts.Security;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Runtime.CurrentActor;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class SecuritySurfaceFullCoverageTests
{
    [Fact]
    public async Task Snapshot_CoversInvalidHasAnyAllCacheKeysAndDefaultRefresh()
    {
        var authenticatedWithoutUser = Snapshot(userId: null, accessVersion: -2);
        var inactive = Snapshot(isActive: false);

        authenticatedWithoutUser.AccessCacheKey.Should().Be("authenticated-without-user");
        inactive.AccessCacheKey.Should().Be("inactive");
        PermissionSnapshot.Anonymous.AccessCacheKey.Should().Be("anonymous");
        authenticatedWithoutUser.HasAny(" ", "view").Should().BeFalse();
        authenticatedWithoutUser.HasAny("document.kind", "view").Should().BeFalse();
        authenticatedWithoutUser.HasAny("document", " ").Should().BeFalse();
        authenticatedWithoutUser.HasAny("document", "view.kind").Should().BeFalse();
        authenticatedWithoutUser.HasAny("missing", "view").Should().BeFalse();
        new PermissionSnapshot(null, "subject", false, true, false, 1, []).HasAny("document", "view")
            .Should().BeFalse();

        IPermissionSnapshotProvider provider = new DefaultRefreshProvider(authenticatedWithoutUser);
        (await provider.RefreshCurrentAsync(default)).Should().BeSameAs(authenticatedWithoutUser);
    }

    [Fact]
    public async Task CurrentAccess_CoversEverySnapshotGuardAndDeterministicRolePermissionOrdering()
    {
        var roleRepository = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        foreach (var snapshot in new[]
                 {
                     PermissionSnapshot.Anonymous,
                     Snapshot(userId: null),
                     Snapshot(isActive: false)
                 })
        {
            var snapshots = new Mock<IPermissionSnapshotProvider>();
            snapshots.Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
            (await new CurrentAccessService(snapshots.Object, roleRepository.Object)
                    .GetCurrentAccessAsync(default))
                .Roles.Should().BeEmpty();
        }

        var userId = Guid.NewGuid();
        var active = Snapshot(
            userId,
            permissions:
            [
                new NgbPermissionKey("report", "z", "view"),
                new NgbPermissionKey("catalog", "b", "view"),
                new NgbPermissionKey("catalog", "a", "manage")
            ]);
        var now = DateTime.UtcNow;
        var roles = new[]
        {
            new PlatformRole(Guid.NewGuid(), "z", "Same", null, false, true, now, now),
            new PlatformRole(Guid.NewGuid(), "a", "Same", null, true, true, now, now)
        };
        roleRepository.Setup(x => x.GetRolesForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);
        var activeProvider = new Mock<IPermissionSnapshotProvider>();
        activeProvider.Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);

        var result = await new CurrentAccessService(activeProvider.Object, roleRepository.Object)
            .GetCurrentAccessAsync(default);

        result.Roles.Select(x => x.Code).Should().Equal("a", "z");
        result.Permissions.Select(x => $"{x.ResourceKind}.{x.ResourceCode}.{x.ActionCode}")
            .Should().Equal("catalog.a.manage", "catalog.b.view", "report.z.view");
    }

    [Fact]
    public async Task EffectiveAccess_CoversMissingUserVersionFallbackGroupingNamesAndGrantedActions()
    {
        var userId = Guid.NewGuid();
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users.SetupSequence(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null)
            .ReturnsAsync(User(userId))
            .ReturnsAsync(User(userId));
        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions.SetupSequence(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserAccessVersion?)null)
            .ReturnsAsync(new PlatformUserAccessVersion(userId, 8, DateTime.UtcNow));
        var permissions = new Mock<IPermissionSnapshotRepository>(MockBehavior.Strict);
        permissions.Setup(x => x.GetEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NgbPermissionKey("catalog", "customer", "view"),
                new NgbPermissionKey("report", "balance", "execute"),
                new NgbPermissionKey("report", "balance", "view"),
                new NgbPermissionKey("report", "alpha", "view")
            ]);
        using var registry = new PermissionDefinitionRegistry([
            new Source([
                new("report", "balance", "view", "Balance: View", "Reports"),
                new("report", "balance", "execute", "Balance: Execute", "Reports"),
                new("report", "alpha", "view", "Alpha: View", "Reports"),
                new("catalog", "customer", "edit", "No marker", "Catalogs"),
                new("catalog", "customer", "view", "Customer: View", "Catalogs")
            ])
        ]);
        var sut = new EffectiveAccessService(users.Object, versions.Object, permissions.Object, registry);

        await ((Func<Task>)(() => sut.GetEffectiveAccessAsync(userId, default)))
            .Should().ThrowAsync<SecurityUserNotFoundException>();
        var fallback = await sut.GetEffectiveAccessAsync(userId, default);
        var versioned = await sut.GetEffectiveAccessAsync(userId, default);

        fallback.AccessVersion.Should().Be(1);
        versioned.AccessVersion.Should().Be(8);
        fallback.Groups.Select(x => x.Group).Should().Equal("Catalogs", "Reports");
        fallback.Groups[0].Resources.Should().ContainSingle(x =>
            x.ResourceCode == "customer" && x.DisplayName == "customer" && x.Actions.SequenceEqual(new[] { "view" }));
        fallback.Groups[1].Resources.Select(x => x.DisplayName).Should().Equal("Alpha", "Balance");
        fallback.Groups[1].Resources.Single(x => x.DisplayName == "Balance").Actions
            .Should().Equal("execute", "view");
    }

    [Fact]
    public async Task PermissionDefinitionSources_CoverMetadataPlatformReportsSortingLabelsAndFallbackGroups()
    {
        var documents = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        documents.Setup(x => x.GetAll()).Returns([
            new DocumentTypeMetadata("z-document", [], null),
            new DocumentTypeMetadata("a-document", [], new DocumentPresentationMetadata("A document"))
        ]);
        var catalogs = new Mock<ICatalogTypeRegistry>(MockBehavior.Strict);
        catalogs.Setup(x => x.All()).Returns([
            Catalog("z-catalog", "Z catalog"),
            Catalog("a-catalog", "A catalog")
        ]);
        var metadata = await new MetadataPermissionDefinitionSource(documents.Object, catalogs.Object)
            .GetDefinitionsAsync(default);

        metadata.Should().HaveCount(28 + 14);
        metadata.First().ResourceCode.Should().Be("a-document");
        metadata.Should().Contain(x => x.DisplayName == "Z catalog: View Audit");

        var platform = await new PlatformPermissionDefinitionSource().GetDefinitionsAsync(default);
        platform.Should().HaveCount(15);
        platform.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.Group));

        var reportProvider = new Mock<IReportDefinitionProvider>(MockBehavior.Strict);
        reportProvider.Setup(x => x.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            new ReportDefinitionDto("z-report", "Z report"),
            new ReportDefinitionDto("a-report", "A report", "Accounting")
        ]);
        var reports = await new ReportPermissionDefinitionSource(reportProvider.Object)
            .GetDefinitionsAsync(default);
        reports.Should().HaveCount(12);
        reports.First().Should().Match<PermissionDefinitionDto>(x =>
            x.ResourceCode == "a-report" && x.Group == "Accounting");
        reports.Should().Contain(x => x.ResourceCode == "z-report" && x.Group == "Reports"
                                                       && x.DisplayName == "Z report: Delete Variant");
    }

    [Fact]
    public async Task Registry_CoversWaitingCacheDuplicateFirstWinsFullOrderingAndDispose()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registry = new PermissionDefinitionRegistry([
            new BlockingSource(entered, release, [
                new(" report ", " b ", " view ", " B ", " z "),
                new(" catalog ", " c ", " edit ", " C ", " A "),
                new(" catalog ", " a ", " view ", " A ", " A "),
                new(" catalog ", " a ", " manage ", " A manage ", " A "),
                new(" CATALOG ", " A ", " VIEW ", " duplicate ", " duplicate ")
            ])
        ]);

        var firstTask = registry.GetAllAsync(default);
        await entered.Task;
        var waitingTask = registry.GetAllAsync(default);
        release.SetResult();
        var first = await firstTask;
        var waiting = await waitingTask;

        waiting.Should().BeSameAs(first);
        first.Should().HaveCount(4);
        first.Select(x => $"{x.Group}:{x.ResourceKind}:{x.ResourceCode}:{x.ActionCode}").Should().Equal(
            "A:catalog:a:manage",
            "A:catalog:a:view",
            "A:catalog:c:edit",
            "z:report:b:view");
        first.Single(x => x.ResourceCode == "a" && x.ActionCode == "view").DisplayName.Should().Be("A");
    }

    [Fact]
    public async Task SecurityCache_CoversEveryCacheFamilyNormalizationVersionFloorAndCancellation()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = Cache(memory);
        var snapshot = Snapshot(accessVersion: 4);
        var calls = 0;

        await cache.GetOrCreatePermissionSnapshotAsync(Guid.Empty, -2, _ => Task.FromResult(++calls), default);
        await cache.GetOrCreatePermissionSnapshotAsync(Guid.Empty, 0, _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateMainMenuAsync(snapshot, _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateMainMenuAsync(snapshot, _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateCatalogMetadataAsync(snapshot, _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateCatalogMetadataAsync(snapshot, _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateCatalogTypeMetadataAsync(snapshot, " Customer ", _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateCatalogTypeMetadataAsync(snapshot, "customer", _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateDocumentMetadataAsync(snapshot, _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateDocumentMetadataAsync(snapshot, _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateDocumentTypeMetadataAsync(snapshot, " Invoice ", _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateDocumentTypeMetadataAsync(snapshot, "invoice", _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateReportDefinitionsAsync(snapshot, _ => Task.FromResult(++calls), default);
        await cache.GetOrCreateReportDefinitionsAsync(snapshot, _ => Task.FromResult(++calls), default);

        calls.Should().Be(7);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await ((Func<Task>)(() => cache.GetOrCreateMainMenuAsync(
                snapshot, _ => Task.FromResult(1), cancelled.Token)))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void SecurityCacheOptionsValidator_CoversSuccessLowerAndUpperBoundFailures()
    {
        var validator = new NgbSecurityCacheOptionsValidator();
        validator.Validate(null, new NgbSecurityCacheOptions()).Succeeded.Should().BeTrue();

        var invalid = validator.Validate(null, new NgbSecurityCacheOptions
        {
            PermissionSnapshotTtl = TimeSpan.Zero,
            MainMenuTtl = TimeSpan.FromHours(2),
            CatalogMetadataTtl = TimeSpan.Zero,
            DocumentMetadataTtl = TimeSpan.FromHours(2),
            ReportDefinitionsTtl = TimeSpan.Zero
        });
        invalid.Failed.Should().BeTrue();
        invalid.Failures.Should().HaveCount(5);
    }

    [Fact]
    public async Task SnapshotProvider_CoversInactiveUnknownNonBootstrapInactiveUserVersionFloorAndRetryAfterFailure()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var repository = new Mock<IPermissionSnapshotRepository>(MockBehavior.Strict);
        var inactiveActor = Provider(
            new ActorIdentity("inactive", null, null, IsActive: false), repository.Object, memory);
        (await inactiveActor.GetCurrentAsync(default)).AccessCacheKey.Should().Be("inactive");

        repository.Setup(x => x.GetUserAccessStateByAuthSubjectAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserAccessState?)null);
        var unknown = await Provider(new ActorIdentity("unknown", null, null), repository.Object, memory)
            .GetCurrentAsync(default);
        unknown.IsActive.Should().BeFalse();

        var inactiveUserId = Guid.NewGuid();
        repository.Setup(x => x.GetUserAccessStateByAuthSubjectAsync("inactive-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserAccessState(inactiveUserId, "inactive-user", null, null, false, 9));
        var inactiveUser = await Provider(new ActorIdentity("inactive-user", null, null), repository.Object, memory)
            .GetCurrentAsync(default);
        inactiveUser.UserId.Should().Be(inactiveUserId);
        inactiveUser.IsActive.Should().BeFalse();

        var activeId = Guid.NewGuid();
        repository.SetupSequence(x => x.GetUserAccessStateByAuthSubjectAsync("retry", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"))
            .ReturnsAsync(new PlatformUserAccessState(activeId, "retry", null, null, true, 0));
        repository.Setup(x => x.GetEffectivePermissionsAsync(activeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var retry = Provider(new ActorIdentity("retry", null, null), repository.Object, memory);
        await ((Func<Task>)(() => retry.GetCurrentAsync(default))).Should().ThrowAsync<InvalidOperationException>();
        var recovered = await retry.GetCurrentAsync(default);
        recovered.AccessVersion.Should().Be(1);
    }

    [Fact]
    public async Task AccessChecker_CoversSuccessfulRequire()
    {
        var provider = new Mock<IPermissionSnapshotProvider>();
        provider.Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(
            permissions: [new NgbPermissionKey("document", "invoice", "view")]));

        await new NgbAccessChecker(provider.Object)
            .RequireAsync("document", "invoice", "view", default);
    }

    private static PermissionSnapshot Snapshot(
        Guid? userId = null,
        bool isActive = true,
        long accessVersion = 1,
        IReadOnlyCollection<NgbPermissionKey>? permissions = null)
        => new(
            userId,
            "subject",
            isAuthenticated: true,
            isActive,
            isBootstrapAdmin: false,
            accessVersion,
            permissions ?? []);

    private static PlatformUser User(Guid id)
        => new(id, "subject", null, "User", true, DateTime.UtcNow, DateTime.UtcNow);

    private static CatalogTypeMetadata Catalog(string code, string name)
        => new(code, name, [], new CatalogPresentationMetadata("table", "name"), new CatalogMetadataVersion(1, "hash"));

    private static NgbSecurityCache Cache(IMemoryCache memory)
        => new(memory, new OptionsMonitor(new NgbSecurityCacheOptions()));

    private static PermissionSnapshotProvider Provider(
        ActorIdentity actor,
        IPermissionSnapshotRepository repository,
        IMemoryCache memory)
        => new(new ActorContext(actor), repository, Cache(memory));

    private sealed class DefaultRefreshProvider(PermissionSnapshot snapshot) : IPermissionSnapshotProvider
    {
        public Task<PermissionSnapshot> GetCurrentAsync(CancellationToken ct) => Task.FromResult(snapshot);
    }

    private sealed class ActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current { get; } = actor;
    }

    private sealed class Source(IReadOnlyList<PermissionDefinitionDto> definitions) : INgbPermissionDefinitionSource
    {
        public Task<IReadOnlyList<PermissionDefinitionDto>> GetDefinitionsAsync(CancellationToken ct)
            => Task.FromResult(definitions);
    }

    private sealed class BlockingSource(
        TaskCompletionSource entered,
        TaskCompletionSource release,
        IReadOnlyList<PermissionDefinitionDto> definitions) : INgbPermissionDefinitionSource
    {
        public async Task<IReadOnlyList<PermissionDefinitionDto>> GetDefinitionsAsync(CancellationToken ct)
        {
            entered.SetResult();
            await release.Task;
            return definitions;
        }
    }

    private sealed class OptionsMonitor(NgbSecurityCacheOptions value) : IOptionsMonitor<NgbSecurityCacheOptions>
    {
        public NgbSecurityCacheOptions CurrentValue { get; } = value;
        public NgbSecurityCacheOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<NgbSecurityCacheOptions, string?> listener) => null;
    }
}
