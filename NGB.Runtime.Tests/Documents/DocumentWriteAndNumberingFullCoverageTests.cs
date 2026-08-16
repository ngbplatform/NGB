using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Numbering;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentWriteAndNumberingFullCoverageTests
{
    [Fact]
    public async Task WriteEngine_CreateAndDeleteCoverOptionalLockMissingAndResolvedStorage()
    {
        var id = Guid.NewGuid();
        var fixture = new WriteFixture();
        var storage = new BasicStorage("doc");
        fixture.Resolver.SetupSequence(x => x.TryResolve("doc"))
            .Returns((IDocumentTypeStorage?)null)
            .Returns(storage)
            .Returns((IDocumentTypeStorage?)null)
            .Returns(storage);

        await fixture.Sut.EnsureDraftStorageCreatedAsync(id, "doc", acquireLock: false);
        await fixture.Sut.EnsureDraftStorageCreatedAsync(id, "doc", acquireLock: true);
        await fixture.Sut.DeleteDraftStorageAsync(id, "doc", acquireLock: false);
        await fixture.Sut.DeleteDraftStorageAsync(id, "doc", acquireLock: true);

        storage.Created.Should().Be(1);
        storage.Deleted.Should().Be(1);
        fixture.Locks.Verify(x => x.LockDocumentAsync(id, It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Uow.Verify(x => x.EnsureActiveTransaction(), Times.Exactly(4));
    }

    [Fact]
    public async Task WriteEngine_UpdateCoversNullOptionalLockMissingBasicAndFullStorage()
    {
        var fixture = new WriteFixture();
        await ((Func<Task>)(() => fixture.Sut.UpdateDraftStorageAsync(null!, false)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        var record = Document(number: "N-1");
        var full = new FullStorage("doc");
        fixture.Resolver.SetupSequence(x => x.TryResolve("doc"))
            .Returns((IDocumentTypeStorage?)null)
            .Returns(new BasicStorage("doc"))
            .Returns(full);

        await fixture.Sut.UpdateDraftStorageAsync(record, acquireLock: false);
        await fixture.Sut.UpdateDraftStorageAsync(record, acquireLock: false);
        await fixture.Sut.UpdateDraftStorageAsync(record, acquireLock: true);

        full.Updated.Should().BeSameAs(record);
        fixture.Locks.Verify(x => x.LockDocumentAsync(record.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NumberingSync_RejectsNullAndSkipsSyncForExistingNumberOrBlankAssignment()
    {
        var numbering = new Mock<IDocumentNumberingService>(MockBehavior.Strict);
        var fixture = new WriteFixture();
        var sut = new DocumentNumberingAndTypedSyncService(numbering.Object, fixture.Sut);
        await ((Func<Task>)(() => sut.EnsureNumberAndSyncTypedAsync(null!, DateTime.UtcNow)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        var withNumber = Document("N-1");
        numbering.Setup(x => x.EnsureNumberAsync(withNumber, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("N-1");
        (await sut.EnsureNumberAndSyncTypedAsync(withNumber, DateTime.UtcNow)).Should().Be("N-1");

        var withoutNumber = Document(number: null);
        numbering.Setup(x => x.EnsureNumberAsync(withoutNumber, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(" ");
        (await sut.EnsureNumberAndSyncTypedAsync(withoutNumber, DateTime.UtcNow)).Should().Be(" ");
        fixture.Resolver.Verify(x => x.TryResolve(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task NumberingSync_NewAssignmentCopiesEveryHeaderFieldIntoTypedUpdater()
    {
        var numbering = new Mock<IDocumentNumberingService>(MockBehavior.Strict);
        var fixture = new WriteFixture();
        var storage = new FullStorage("doc");
        fixture.Resolver.Setup(x => x.TryResolve("doc")).Returns(storage);
        var original = Document(number: null, status: DocumentStatus.MarkedForDeletion);
        numbering.Setup(x => x.EnsureNumberAsync(original, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("AUTO-1");
        var sut = new DocumentNumberingAndTypedSyncService(numbering.Object, fixture.Sut);

        (await sut.EnsureNumberAndSyncTypedAsync(original, DateTime.UtcNow)).Should().Be("AUTO-1");

        storage.Updated.Should().NotBeSameAs(original).And.BeEquivalentTo(original, options => options
            .Excluding(x => x.Number));
        storage.Updated!.Number.Should().Be("AUTO-1");
    }

    private static DocumentRecord Document(
        string? number = "N-1",
        DocumentStatus status = DocumentStatus.Draft)
        => new()
        {
            Id = Guid.NewGuid(),
            TypeCode = "doc",
            Number = number,
            DateUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Status = status,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            PostedAtUtc = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            MarkedForDeletionAtUtc = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)
        };

    private sealed class WriteFixture
    {
        public WriteFixture()
        {
            Uow.Setup(x => x.EnsureActiveTransaction());
            Locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Sut = new DocumentWriteEngine(Uow.Object, Locks.Object, Resolver.Object);
        }

        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IAdvisoryLockManager> Locks { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentTypeStorageResolver> Resolver { get; } = new(MockBehavior.Loose);
        public DocumentWriteEngine Sut { get; }
    }

    private class BasicStorage(string typeCode) : IDocumentTypeStorage
    {
        public string TypeCode { get; } = typeCode;
        public int Created { get; private set; }
        public int Deleted { get; private set; }
        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            Created++;
            return Task.CompletedTask;
        }

        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            Deleted++;
            return Task.CompletedTask;
        }
    }

    private sealed class FullStorage(string typeCode) : BasicStorage(typeCode), IDocumentTypeDraftFullUpdater
    {
        public DocumentRecord? Updated { get; private set; }
        public Task UpdateDraftAsync(DocumentRecord updatedDraft, CancellationToken ct = default)
        {
            Updated = updatedDraft;
            return Task.CompletedTask;
        }
    }
}
