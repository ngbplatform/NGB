using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Effects;
using NGB.Contracts.Graph;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.Security;
using NGB.Runtime.Documents;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class PermissionAwareDocumentServiceFullCoverageTests
{
    [Fact]
    public async Task Metadata_CoversNoViewDeniedAllowedFilteringActionsCapabilitiesAndCache()
    {
        var inner = new Mock<IDocumentService>(MockBehavior.Strict);
        inner.Setup(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            Metadata("doc", new DocumentCapabilitiesDto(SupportsActions: true),
                [new("post", "Post"), new("unpost", "Unpost")]),
            Metadata("other")
        ]);
        inner.Setup(x => x.GetTypeMetadataAsync("doc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Metadata("doc", new DocumentCapabilitiesDto(SupportsActions: true),
                [new("post", "Post"), new("unpost", "Unpost")]));
        var allowed = Snapshot(1,
            Key("doc", NgbPermissionActions.View),
            Key("doc", NgbPermissionActions.Create),
            Key("doc", NgbPermissionActions.Post));
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.SetupSequence(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionSnapshot.Anonymous)
            .ReturnsAsync(allowed)
            .ReturnsAsync(PermissionSnapshot.Anonymous)
            .ReturnsAsync(allowed)
            .ReturnsAsync(allowed);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var sut = new PermissionAwareDocumentService(inner.Object, access.Object, Cache(memory));

        (await sut.GetAllMetadataAsync(default)).Should().BeEmpty();
        var filtered = await sut.GetAllMetadataAsync(default);
        filtered.Should().ContainSingle().Which.Should().Match<DocumentTypeMetadataDto>(x =>
            x.DocumentType == "doc"
            && x.Actions!.Count == 1 && x.Actions[0].Code == "post"
            && x.Capabilities!.CanCreate && x.Capabilities.CanPost
            && !x.Capabilities.CanEditDraft && !x.Capabilities.CanUnpost
            && x.Capabilities.SupportsActions);
        await ((Func<Task>)(() => sut.GetTypeMetadataAsync("doc", default)))
            .Should().ThrowAsync<NgbPermissionDeniedException>();
        var type = await sut.GetTypeMetadataAsync("doc", default);
        type.Actions.Should().ContainSingle(x => x.Code == "post");
        (await sut.GetTypeMetadataAsync("doc", default)).Should().BeSameAs(type);
        inner.Verify(x => x.GetAllMetadataAsync(It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(x => x.GetTypeMetadataAsync("doc", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Metadata_NullCacheResultsUseEmptyAndThrowGuard()
    {
        var inner = new Mock<IDocumentService>(MockBehavior.Strict);
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(1, Key("doc", NgbPermissionActions.View)));
        using var memory = new NullMemoryCache();
        var sut = new PermissionAwareDocumentService(inner.Object, access.Object, Cache(memory));

        (await sut.GetAllMetadataAsync(default)).Should().BeEmpty();
        await ((Func<Task>)(() => sut.GetTypeMetadataAsync("doc", default)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Capabilities_CoverNullEmptyFilteredActionsAllPermissionsAndExistingRestrictions()
    {
        var allowAll = Snapshot(1,
            Key("doc", NgbPermissionActions.Create),
            Key("doc", NgbPermissionActions.EditDraft),
            Key("doc", NgbPermissionActions.DeleteDraft),
            Key("doc", NgbPermissionActions.Post),
            Key("doc", NgbPermissionActions.Unpost),
            Key("doc", NgbPermissionActions.Repost),
            Key("doc", NgbPermissionActions.MarkForDeletion),
            Key("doc", NgbPermissionActions.ViewEffects),
            Key("doc", NgbPermissionActions.ViewFlow),
            Key("doc", "custom"));
        var noActions = Metadata("doc", capabilities: null, actions: null);
        var emptyActions = Metadata("doc", new DocumentCapabilitiesDto(SupportsActions: true), []);
        var actions = Metadata("doc", actions: [new("custom", "Custom"), new("denied", "Denied")]);
        var restricted = actions with
        {
            Capabilities = new DocumentCapabilitiesDto(
                CanCreate: false,
                CanEditDraft: false,
                CanDeleteDraft: false,
                CanPost: false,
                CanUnpost: false,
                CanRepost: false,
                CanMarkForDeletion: false,
                SupportsActions: false,
                CanViewEffects: false,
                CanViewFlow: false)
        };

        PermissionAwareDocumentService.ApplyCapabilities(noActions, allowAll).Should().Match<DocumentTypeMetadataDto>(x =>
            x.Actions!.Count == 0 && !x.Capabilities!.SupportsActions
            && x.Capabilities.CanCreate && x.Capabilities.CanEditDraft && x.Capabilities.CanDeleteDraft
            && x.Capabilities.CanPost && x.Capabilities.CanUnpost && x.Capabilities.CanRepost
            && x.Capabilities.CanMarkForDeletion && x.Capabilities.CanViewEffects && x.Capabilities.CanViewFlow);
        PermissionAwareDocumentService.ApplyCapabilities(emptyActions, allowAll).Actions.Should().BeEmpty();
        PermissionAwareDocumentService.ApplyCapabilities(actions, allowAll).Actions
            .Should().ContainSingle(x => x.Code == "custom");
        PermissionAwareDocumentService.ApplyCapabilities(restricted, allowAll).Capabilities
            .Should().Be(new DocumentCapabilitiesDto(false, false, false, false, false, false, false, false, false, false));
    }

    [Fact]
    public async Task EveryEndpoint_RequiresExactPermissionAndDelegatesArgumentsAndResults()
    {
        var id = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var payload = new RecordPayload();
        var item = Item(id);
        var inner = new Mock<IDocumentService>(MockBehavior.Strict);
        inner.Setup(x => x.GetPageAsync("doc", It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<DocumentDto>([], 0, 50, 0));
        inner.Setup(x => x.GetByIdAsync("doc", id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        inner.Setup(x => x.LookupAcrossTypesAsync(
                It.Is<IReadOnlyList<string>>(types => types.SequenceEqual(new[] { "doc" })),
                "q", 3, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DocumentLookupDto(id, "doc", "Lookup", DocumentStatus.Draft, false)]);
        inner.Setup(x => x.GetByIdsAcrossTypesAsync(
                It.Is<IReadOnlyList<string>>(types => types.SequenceEqual(new[] { "doc" })),
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { id })), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DocumentLookupDto(id, "doc", "Lookup", DocumentStatus.Draft, false)]);
        inner.Setup(x => x.CreateDraftAsync("doc", payload, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        inner.Setup(x => x.UpdateDraftAsync("doc", id, payload, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        inner.Setup(x => x.DeleteDraftAsync("doc", id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        inner.Setup(x => x.ExecuteActionAsync("doc", id, "custom", It.IsAny<CancellationToken>())).ReturnsAsync(item);
        inner.Setup(x => x.GetRelationshipGraphAsync("doc", id, 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipGraphDto([], []));
        inner.Setup(x => x.GetEffectsAsync("doc", id, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEffectsDto([], [], []));
        inner.Setup(x => x.DeriveAsync("doc", sourceId, "copy", payload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.RequireAsync(NgbResourceKinds.Document, "doc", It.IsAny<string>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        access.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(1, Key("doc", NgbPermissionActions.Lookup)));
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var sut = new PermissionAwareDocumentService(inner.Object, access.Object, Cache(memory));

        (await sut.GetPageAsync("doc", new PageRequestDto(), default)).Items.Should().BeEmpty();
        (await sut.GetByIdAsync("doc", id, default)).Should().Be(item);
        (await sut.CreateDraftAsync("doc", payload, default)).Should().Be(item);
        (await sut.UpdateDraftAsync("doc", id, payload, default)).Should().Be(item);
        await sut.DeleteDraftAsync("doc", id, default);
        (await sut.ExecuteActionAsync("doc", id, "custom", default)).Should().Be(item);
        (await sut.GetRelationshipGraphAsync("doc", id, 2, 10, default)).Nodes.Should().BeEmpty();
        (await sut.GetEffectsAsync("doc", id, 20, default)).AccountingEntries.Should().BeEmpty();
        (await sut.DeriveAsync("doc", sourceId, "copy", payload, default)).Should().Be(item);

        (await sut.LookupAcrossTypesAsync(null!, null, 1, false, default)).Should().BeEmpty();
        (await sut.LookupAcrossTypesAsync([], null, 1, false, default)).Should().BeEmpty();
        (await sut.GetByIdsAcrossTypesAsync(null!, [id], default)).Should().BeEmpty();
        (await sut.GetByIdsAcrossTypesAsync([], [id], default)).Should().BeEmpty();
        (await sut.LookupAcrossTypesAsync(["doc", "DOC", "denied"], "q", 3, true, default))
            .Should().ContainSingle();
        (await sut.GetByIdsAcrossTypesAsync(["doc", "DOC", "denied"], [id], default))
            .Should().ContainSingle();

        foreach (var action in new[]
                 {
                     NgbPermissionActions.View, NgbPermissionActions.Create, NgbPermissionActions.EditDraft,
                     NgbPermissionActions.DeleteDraft, "custom", NgbPermissionActions.ViewFlow,
                     NgbPermissionActions.ViewEffects
                 })
        {
            access.Verify(x => x.RequireAsync(
                NgbResourceKinds.Document, "doc", action, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }
    }

    [Fact]
    public async Task FilteredLookupsReturnEmptyAndRequireFailurePropagates()
    {
        var inner = new Mock<IDocumentService>(MockBehavior.Strict);
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(1));
        access.Setup(x => x.RequireAsync(NgbResourceKinds.Document, "doc", NgbPermissionActions.View,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NgbPermissionDeniedException(
                new NgbPermissionKey(NgbResourceKinds.Document, "doc", NgbPermissionActions.View)));
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var sut = new PermissionAwareDocumentService(inner.Object, access.Object, Cache(memory));

        (await sut.LookupAcrossTypesAsync(["doc", "DOC"], null, 2, false, default)).Should().BeEmpty();
        (await sut.GetByIdsAcrossTypesAsync(["doc", "DOC"], [Guid.NewGuid()], default)).Should().BeEmpty();
        await ((Func<Task>)(() => sut.GetPageAsync("doc", new PageRequestDto(), default)))
            .Should().ThrowAsync<NgbPermissionDeniedException>();
    }

    private static DocumentTypeMetadataDto Metadata(
        string type,
        DocumentCapabilitiesDto? capabilities = null,
        IReadOnlyList<ActionMetadataDto>? actions = null)
        => new(type, type, EntityKind.Document, Actions: actions, Capabilities: capabilities);

    private static DocumentDto Item(Guid id)
        => new(id, "Document", new RecordPayload(), DocumentStatus.Draft, false);

    private static NgbSecurityCache Cache(IMemoryCache memory)
        => new(memory, new OptionsMonitor(new NgbSecurityCacheOptions()));

    private static PermissionSnapshot Snapshot(long version, params NgbPermissionKey[] permissions)
        => new(Guid.NewGuid(), "subject", true, true, false, version, permissions);

    private static NgbPermissionKey Key(string type, string action)
        => new(NgbResourceKinds.Document, type, action);

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
