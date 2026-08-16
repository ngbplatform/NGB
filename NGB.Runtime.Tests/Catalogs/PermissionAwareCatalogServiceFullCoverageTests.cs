using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Core.Security;
using NGB.Persistence.Catalogs.Universal;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Catalogs;

public sealed class PermissionAwareCatalogServiceFullCoverageTests
{
    [Fact]
    public async Task Metadata_CoversNoViewDeniedAllowedFilteringCapabilitiesAndCaching()
    {
        var fixture = new CatalogServiceTestFixture();
        fixture.AddMetadata(CatalogServiceTestFixture.RichMetadata("other"));
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        var allowed = Snapshot(1,
            Key("rich", NgbPermissionActions.View),
            Key("rich", NgbPermissionActions.Create),
            Key("rich", NgbPermissionActions.MarkForDeletion));
        access.SetupSequence(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionSnapshot.Anonymous)
            .ReturnsAsync(allowed)
            .ReturnsAsync(PermissionSnapshot.Anonymous)
            .ReturnsAsync(allowed)
            .ReturnsAsync(allowed);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var sut = new PermissionAwareCatalogService(fixture.CreateService(), access.Object, Cache(memory));

        (await sut.GetAllMetadataAsync(default)).Should().BeEmpty();
        var filtered = await sut.GetAllMetadataAsync(default);
        filtered.Should().ContainSingle().Which.Should().Match<NGB.Contracts.Metadata.CatalogTypeMetadataDto>(x =>
            x.CatalogType == "rich"
            && x.Capabilities!.CanCreate
            && !x.Capabilities.CanEdit
            && !x.Capabilities.CanDelete
            && x.Capabilities.CanMarkForDeletion);

        await ((Func<Task>)(() => sut.GetTypeMetadataAsync("rich", default)))
            .Should().ThrowAsync<NgbPermissionDeniedException>();
        var type = await sut.GetTypeMetadataAsync("rich", default);
        type.Capabilities.Should().Be(filtered[0].Capabilities);
        (await sut.GetTypeMetadataAsync("rich", default)).Should().BeSameAs(type);
    }

    [Fact]
    public async Task Metadata_NullCacheResultsUseEmptyAndThrowGuard()
    {
        var fixture = new CatalogServiceTestFixture();
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        var allowed = Snapshot(1, Key("rich", NgbPermissionActions.View));
        access.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(allowed);
        using var memory = new NullMemoryCache();
        var sut = new PermissionAwareCatalogService(fixture.CreateService(), access.Object, Cache(memory));

        (await sut.GetAllMetadataAsync(default)).Should().BeEmpty();
        await ((Func<Task>)(() => sut.GetTypeMetadataAsync("rich", default)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CrudAndLookups_RequireExactActionsAndDelegateMappedResults()
    {
        var fixture = new CatalogServiceTestFixture();
        var id = Guid.NewGuid();
        var createdId = Guid.NewGuid();
        fixture.Reader.Setup(x => x.CountAsync(It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(0);
        fixture.Reader.Setup(x => x.GetPageAsync(It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogHeadDescriptor _, Guid rowId, CancellationToken _) =>
                CatalogServiceTestFixture.Row(rowId,
                    new Dictionary<string, object?> { ["display"] = "Item" }));
        fixture.Reader.Setup(x => x.LookupAsync(It.IsAny<CatalogHeadDescriptor>(), "q", 2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CatalogLookupRow(id, "Lookup")]);
        fixture.Reader.Setup(x => x.GetByIdsAsync(It.IsAny<CatalogHeadDescriptor>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CatalogLookupRow(id, "By id")]);
        fixture.Reader.Setup(x => x.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<CatalogHeadDescriptor>>(),
                "q", 3, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CatalogLookupSearchRow(id, "rich", "Across", false)]);
        fixture.Drafts.Setup(x => x.CreateHeaderOnlyAsync("rich", false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdId);
        fixture.Repository.Setup(x => x.GetForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid rowId, CancellationToken _) => CatalogServiceTestFixture.Record(rowId));

        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.RequireAsync(NgbResourceKinds.Catalog, "rich", It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        access.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(1, Key("rich", NgbPermissionActions.Lookup)));
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var sut = new PermissionAwareCatalogService(fixture.CreateService(), access.Object, Cache(memory));

        (await sut.GetPageAsync("rich", new PageRequestDto(), default)).Items.Should().BeEmpty();
        (await sut.GetByIdAsync("rich", id, default)).Id.Should().Be(id);
        (await sut.CreateAsync("rich", new RecordPayload(new Dictionary<string, System.Text.Json.JsonElement>
            { ["display"] = CatalogServiceTestFixture.Json("Created") }), default)).Id.Should().Be(createdId);
        (await sut.UpdateAsync("rich", id, new RecordPayload(), default)).Id.Should().Be(id);
        await sut.MarkForDeletionAsync("rich", id, default);
        await sut.UnmarkForDeletionAsync("rich", id, default);
        (await sut.LookupAsync("rich", "q", 2, default)).Should().ContainSingle();
        (await sut.GetByIdsAsync("rich", [id], default)).Should().ContainSingle();

        (await sut.LookupAcrossTypesAsync(null!, null, 1, false, default)).Should().BeEmpty();
        (await sut.LookupAcrossTypesAsync([], null, 1, false, default)).Should().BeEmpty();
        var across = await sut.LookupAcrossTypesAsync(["rich", "RICH", "denied"], "q", 3, true, default);
        across.Should().ContainSingle();
        fixture.Reader.Verify(x => x.LookupAcrossTypesAsync(
            It.Is<IReadOnlyList<CatalogHeadDescriptor>>(heads => heads.Count == 1 && heads[0].CatalogCode == "rich"),
            "q", 3, true, It.IsAny<CancellationToken>()), Times.Once);

        foreach (var action in new[]
                 {
                     NgbPermissionActions.View, NgbPermissionActions.View, NgbPermissionActions.Create,
                     NgbPermissionActions.Edit, NgbPermissionActions.MarkForDeletion,
                     NgbPermissionActions.UnmarkForDeletion, NgbPermissionActions.Lookup, NgbPermissionActions.Lookup
                 })
        {
            access.Verify(x => x.RequireAsync(
                NgbResourceKinds.Catalog, "rich", action, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }
    }

    [Fact]
    public async Task LookupAcrossTypes_ReturnsEmptyWhenNothingIsAllowedAndPropagatesRequireFailure()
    {
        var fixture = new CatalogServiceTestFixture();
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(1));
        access.Setup(x => x.RequireAsync(NgbResourceKinds.Catalog, "rich", NgbPermissionActions.View,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NgbPermissionDeniedException(
                new NgbPermissionKey(NgbResourceKinds.Catalog, "rich", NgbPermissionActions.View)));
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var sut = new PermissionAwareCatalogService(fixture.CreateService(), access.Object, Cache(memory));

        (await sut.LookupAcrossTypesAsync(["rich", "RICH"], null, 2, false, default)).Should().BeEmpty();
        await ((Func<Task>)(() => sut.GetPageAsync("rich", new PageRequestDto(), default)))
            .Should().ThrowAsync<NgbPermissionDeniedException>();
        fixture.Reader.Verify(x => x.LookupAcrossTypesAsync(
            It.IsAny<IReadOnlyList<CatalogHeadDescriptor>>(), It.IsAny<string?>(), It.IsAny<int>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Capabilities_NullDefaultsAndExistingRestrictionsAreBothPreserved()
    {
        var allowAll = Snapshot(1,
            Key("rich", NgbPermissionActions.Create),
            Key("rich", NgbPermissionActions.Edit),
            Key("rich", NgbPermissionActions.MarkForDeletion));
        var withoutCapabilities = new CatalogTypeMetadataDto(
            "rich", "Rich", EntityKind.Catalog, Capabilities: null);
        var restricted = withoutCapabilities with
        {
            Capabilities = new CatalogCapabilitiesDto(
                CanCreate: false,
                CanEdit: false,
                CanDelete: true,
                CanMarkForDeletion: false)
        };

        PermissionAwareCatalogService.ApplyCapabilities(withoutCapabilities, allowAll).Capabilities
            .Should().Be(new CatalogCapabilitiesDto(true, true, false, true));
        PermissionAwareCatalogService.ApplyCapabilities(restricted, allowAll).Capabilities
            .Should().Be(new CatalogCapabilitiesDto(false, false, false, false));
    }

    private static NgbSecurityCache Cache(IMemoryCache memory)
        => new(memory, new OptionsMonitor(new NgbSecurityCacheOptions()));

    private static PermissionSnapshot Snapshot(long version, params NgbPermissionKey[] permissions)
        => new(Guid.NewGuid(), "subject", true, true, false, version, permissions);

    private static NgbPermissionKey Key(string code, string action)
        => new(NgbResourceKinds.Catalog, code, action);

    private sealed class OptionsMonitor(NgbSecurityCacheOptions value) : IOptionsMonitor<NgbSecurityCacheOptions>
    {
        public NgbSecurityCacheOptions CurrentValue { get; } = value;
        public NgbSecurityCacheOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<NgbSecurityCacheOptions, string?> listener) => null;
    }

    private sealed class NullMemoryCache : IMemoryCache
    {
        public bool TryGetValue(object key, out object? value)
        {
            value = null;
            return true;
        }

        public ICacheEntry CreateEntry(object key) => throw new NotSupportedException();
        public void Remove(object key) { }
        public void Dispose() { }
    }
}
