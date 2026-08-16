using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Core.Catalogs;
using NGB.Core.Catalogs.Exceptions;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Catalogs;

public sealed class CatalogDraftAndWriteFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public void AuditChanges_CoverNullUndefinedPartsCreateUpdateAddsDeletesAndUnchangedValues()
    {
        var undefined = default(JsonElement);
        var before = Item(new RecordPayload(
            new Dictionary<string, JsonElement>
            {
                ["unchanged"] = Json(" value "),
                ["removed"] = Json(1),
                ["undefined"] = undefined
            },
            new Dictionary<string, RecordPartPayload>
            {
                ["null-part"] = null!,
                ["lines"] = new([
                    new Dictionary<string, JsonElement>
                    {
                        ["amount"] = Json(10),
                        ["undefined"] = undefined
                    }
                ])
            }));
        var after = Item(new RecordPayload(
            new Dictionary<string, JsonElement>
            {
                ["unchanged"] = Json(" value "),
                ["added"] = Json(true)
            },
            new Dictionary<string, RecordPartPayload>
            {
                ["lines"] = new([
                    new Dictionary<string, JsonElement> { ["amount"] = Json(20) },
                    new Dictionary<string, JsonElement> { ["name"] = Json("second") }
                ])
            }));

        var create = CatalogAuditChangeBuilder.BuildCreateChanges(before, "customer");
        create.Select(x => x.FieldPath).Should().Equal(
            "catalog_code", "is_deleted", "removed", "unchanged", "parts.lines[1].amount");

        var update = CatalogAuditChangeBuilder.BuildUpdateChanges(before, after);
        update.Select(x => x.FieldPath).Should().Equal(
            "added", "parts.lines[1].amount", "parts.lines[2].name", "removed");
        update.Single(x => x.FieldPath == "added").OldValueJson.Should().BeNull();
        update.Single(x => x.FieldPath == "removed").NewValueJson.Should().BeNull();

        CatalogAuditChangeBuilder.BuildCreateChanges(Item(new RecordPayload()), "empty")
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task WriteEngine_CoversNoStorageHappyKnownAndUnexpectedFailuresForBothOperations()
    {
        var id = Guid.NewGuid();
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.EnsureActiveTransaction());
        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockCatalogAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var resolver = new Mock<ICatalogTypeStorageResolver>(MockBehavior.Strict);
        resolver.SetupSequence(x => x.TryResolve("cat"))
            .Returns((ICatalogTypeStorage?)null)
            .Returns((ICatalogTypeStorage?)null)
            .Returns(Storage("cat").Object)
            .Returns(Storage("cat").Object);
        var sut = new CatalogWriteEngine(uow.Object, locks.Object, resolver.Object);

        await sut.EnsureStorageCreatedAsync(id, "cat");
        await sut.DeleteStorageAsync(id, "cat");

        var happy = Storage("cat");
        resolver.Setup(x => x.TryResolve("happy")).Returns(happy.Object);
        await sut.EnsureStorageCreatedAsync(id, "happy");
        await sut.DeleteStorageAsync(id, "happy");
        happy.Verify(x => x.EnsureCreatedAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        happy.Verify(x => x.DeleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);

        var known = Storage("cat");
        known.Setup(x => x.EnsureCreatedAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NgbInvariantViolationException("known"));
        known.Setup(x => x.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NgbInvariantViolationException("known"));
        resolver.Setup(x => x.TryResolve("known")).Returns(known.Object);
        await ((Func<Task>)(() => sut.EnsureStorageCreatedAsync(id, "known")))
            .Should().ThrowAsync<NgbInvariantViolationException>();
        await ((Func<Task>)(() => sut.DeleteStorageAsync(id, "known")))
            .Should().ThrowAsync<NgbInvariantViolationException>();

        var broken = Storage("cat");
        broken.Setup(x => x.EnsureCreatedAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ensure failed"));
        broken.Setup(x => x.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("delete failed"));
        resolver.Setup(x => x.TryResolve("broken")).Returns(broken.Object);
        var ensure = await ((Func<Task>)(() => sut.EnsureStorageCreatedAsync(id, "broken")))
            .Should().ThrowAsync<CatalogTypedStorageOperationException>();
        var delete = await ((Func<Task>)(() => sut.DeleteStorageAsync(id, "broken")))
            .Should().ThrowAsync<CatalogTypedStorageOperationException>();
        ensure.Which.Context["operation"].Should().Be("ensure_created");
        delete.Which.Context["operation"].Should().Be("delete");
    }

    [Fact]
    public async Task DraftCreate_CoversGuardsTypedHeaderOnlySuppressAuditAndExternalTransactionOverloads()
    {
        var fixture = new DraftFixture();
        await ((Func<Task>)(() => fixture.Sut.CreateAsync(" ")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.CreateAsync("missing")))
            .Should().ThrowAsync<CatalogTypeNotFoundException>();
        await ((Func<Task>)(() => fixture.Sut.CreateHeaderOnlyAsync(" ")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.CreateHeaderOnlyAsync("missing")))
            .Should().ThrowAsync<CatalogTypeNotFoundException>();

        var created = await fixture.Sut.CreateAsync("cat", manageTransaction: true);
        var suppressed = await fixture.Sut.CreateAsync("cat", manageTransaction: true, suppressAudit: true);
        var header = await fixture.Sut.CreateHeaderOnlyAsync("cat", manageTransaction: true);
        var suppressedHeader = await fixture.Sut.CreateHeaderOnlyAsync(
            "cat", manageTransaction: true, suppressAudit: true);
        var compatibility = await fixture.Sut.CreateAsync("cat", true, default);
        var compatibilityHeader = await fixture.Sut.CreateHeaderOnlyAsync("cat", true, default);
        fixture.Uow.SetupGet(x => x.HasActiveTransaction).Returns(true);
        fixture.Uow.Setup(x => x.EnsureActiveTransaction());
        var external = await fixture.Sut.CreateAsync("cat", manageTransaction: false);
        var externalHeader = await fixture.Sut.CreateHeaderOnlyAsync("cat", manageTransaction: false);

        new[] { created, suppressed, header, suppressedHeader, compatibility, compatibilityHeader, external, externalHeader }
            .Should().OnlyHaveUniqueItems().And.NotContain(Guid.Empty);
        fixture.Repository.Verify(x => x.CreateAsync(
            It.Is<CatalogRecord>(record => record.CatalogCode == "cat"
                                           && record.CreatedAtUtc == Now
                                           && record.UpdatedAtUtc == Now),
            It.IsAny<CancellationToken>()), Times.Exactly(8));
        fixture.Locks.Verify(x => x.LockCatalogAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        fixture.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Catalog, It.IsAny<Guid>(), AuditActionCodes.CatalogCreate,
            It.IsAny<IReadOnlyList<AuditFieldChange>?>(), It.IsAny<object?>(), null,
            It.IsAny<CancellationToken>()), Times.Exactly(6));
    }

    [Fact]
    public async Task DraftDeletion_CoversEmptyMissingIdempotentAndBothStateTransitions()
    {
        var fixture = new DraftFixture();
        var id = Guid.NewGuid();
        await ((Func<Task>)(() => fixture.Sut.MarkForDeletionAsync(Guid.Empty)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.UnmarkForDeletionAsync(Guid.Empty)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        fixture.Repository.SetupSequence(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null)
            .ReturnsAsync(Record(id, deleted: true))
            .ReturnsAsync(Record(id, deleted: false));
        await ((Func<Task>)(() => fixture.Sut.MarkForDeletionAsync(id)))
            .Should().ThrowAsync<CatalogNotFoundException>();
        await fixture.Sut.MarkForDeletionAsync(id);
        await fixture.Sut.MarkForDeletionAsync(id);
        fixture.Repository.Verify(x => x.MarkForDeletionAsync(id, Now, It.IsAny<CancellationToken>()), Times.Once);

        fixture.Repository.SetupSequence(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null)
            .ReturnsAsync(Record(id, deleted: false))
            .ReturnsAsync(Record(id, deleted: true));
        await ((Func<Task>)(() => fixture.Sut.UnmarkForDeletionAsync(id)))
            .Should().ThrowAsync<CatalogNotFoundException>();
        await fixture.Sut.UnmarkForDeletionAsync(id);
        await fixture.Sut.UnmarkForDeletionAsync(id);
        fixture.Repository.Verify(x => x.UnmarkForDeletionAsync(id, Now, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Catalog, id, It.IsAny<string>(), It.IsAny<IReadOnlyList<AuditFieldChange>?>(),
            It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static CatalogItemDto Item(RecordPayload payload)
        => new(Guid.NewGuid(), null, payload, false, false);

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static Mock<ICatalogTypeStorage> Storage(string code)
    {
        var storage = new Mock<ICatalogTypeStorage>(MockBehavior.Loose);
        storage.SetupGet(x => x.CatalogCode).Returns(code);
        storage.Setup(x => x.EnsureCreatedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storage.Setup(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return storage;
    }

    private static CatalogRecord Record(Guid id, bool deleted) => new()
    {
        Id = id,
        CatalogCode = "cat",
        IsDeleted = deleted,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    private sealed class DraftFixture
    {
        public DraftFixture()
        {
            Uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.EnsureActiveTransaction());
            Repository.Setup(x => x.CreateAsync(It.IsAny<CatalogRecord>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Repository.Setup(x => x.MarkForDeletionAsync(
                    It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Repository.Setup(x => x.UnmarkForDeletionAsync(
                    It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Locks.Setup(x => x.LockCatalogAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Resolver.Setup(x => x.TryResolve("cat")).Returns((ICatalogTypeStorage?)null);
            CatalogTypeMetadata? metadata = Metadata();
            Types.Setup(x => x.TryGet("cat", out metadata)).Returns(true);
            CatalogTypeMetadata? missing = null;
            Types.Setup(x => x.TryGet("missing", out missing)).Returns(false);
            Audit.Setup(x => x.WriteAsync(
                    It.IsAny<AuditEntityKind>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<AuditFieldChange>?>(), It.IsAny<object?>(), It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var engine = new CatalogWriteEngine(Uow.Object, Locks.Object, Resolver.Object);
            Sut = new CatalogDraftService(
                Uow.Object,
                Repository.Object,
                engine,
                Types.Object,
                Audit.Object,
                new FixedTimeProvider(Now));
        }

        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<ICatalogRepository> Repository { get; } = new(MockBehavior.Loose);
        public Mock<IAdvisoryLockManager> Locks { get; } = new(MockBehavior.Loose);
        public Mock<ICatalogTypeStorageResolver> Resolver { get; } = new(MockBehavior.Loose);
        public Mock<ICatalogTypeRegistry> Types { get; } = new(MockBehavior.Loose);
        public Mock<IAuditLogService> Audit { get; } = new(MockBehavior.Loose);
        public CatalogDraftService Sut { get; }
    }

    private static CatalogTypeMetadata Metadata()
        => new("cat", "Cat", [], new CatalogPresentationMetadata("cat", "name"), new CatalogMetadataVersion(1, "hash"));

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value);
    }
}
