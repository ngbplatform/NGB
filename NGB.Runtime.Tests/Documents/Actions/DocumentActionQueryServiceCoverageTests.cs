using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Security;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents.Actions;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Documents.Actions;

public sealed class DocumentActionQueryServiceCoverageTests
{
    [Fact]
    public async Task GetEditorState_denies_missing_view_permission_before_data_access()
    {
        var permissions = new Mock<IPermissionSnapshotProvider>(MockBehavior.Strict);
        permissions
            .Setup(provider => provider.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot());
        var repository = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var service = new DocumentActionQueryService(
            null!,
            repository.Object,
            permissions.Object,
            null!,
            uow.Object);

        var action = () => service.GetEditorStateAsync("test.source", Guid.NewGuid(), CancellationToken.None);

        await action.Should().ThrowAsync<NgbPermissionDeniedException>();
        repository.VerifyNoOtherCalls();
        uow.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetEditorState_rejects_document_type_mismatch_inside_transaction()
    {
        var id = Guid.NewGuid();
        var permissions = new Mock<IPermissionSnapshotProvider>(MockBehavior.Strict);
        permissions
            .Setup(provider => provider.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Snapshot(
                    new[]
                {
                    new NgbPermissionKey(
                        NgbResourceKinds.Document,
                        "test.expected",
                        NgbPermissionActions.View)
                }));
        var repository = new Mock<IDocumentRepository>(MockBehavior.Strict);
        repository
            .Setup(repo => repo.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DocumentRecord
                {
                    Id = id,
                    TypeCode = "test.actual",
                    DateUtc = DateTime.UtcNow,
                    Status = DocumentStatus.Draft,
                    Version = 1
                });
        var uow = new Mock<IUnitOfWork>(MockBehavior.Loose);
        uow.SetupGet(unit => unit.HasActiveTransaction).Returns(false);
        var service = new DocumentActionQueryService(
            null!,
            repository.Object,
            permissions.Object,
            null!,
            uow.Object);

        var action = () => service.GetEditorStateAsync("test.expected", id, CancellationToken.None);

        await action.Should().ThrowAsync<DocumentTypeMismatchException>();
        uow.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(unit => unit.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEditorState_rejects_missing_document_inside_transaction()
    {
        var id = Guid.NewGuid();
        var permissions = new Mock<IPermissionSnapshotProvider>(MockBehavior.Strict);
        permissions
            .Setup(provider => provider.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Snapshot(
                    new NgbPermissionKey(
                        NgbResourceKinds.Document,
                        "test.expected",
                        NgbPermissionActions.View)));
        var repository = new Mock<IDocumentRepository>(MockBehavior.Strict);
        repository
            .Setup(repo => repo.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Loose);
        uow.SetupGet(unit => unit.HasActiveTransaction).Returns(false);
        var service = new DocumentActionQueryService(
            null!,
            repository.Object,
            permissions.Object,
            null!,
            uow.Object);

        var action = () => service.GetEditorStateAsync("test.expected", id, CancellationToken.None);

        await action.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task GetEditorState_returns_document_version_and_authorized_actions()
    {
        var harness = new DocumentActionDispatcherCoverageTests.Harness();
        var service = new DocumentActionQueryService(
            harness.DocumentService,
            harness.Documents.Object,
            harness.Permissions.Object,
            harness.Evaluator,
            harness.Uow.Object);

        var state = await service.GetEditorStateAsync(
            harness.Source.TypeCode,
            harness.Source.Id,
            CancellationToken.None);

        state.Document.Id.Should().Be(harness.Source.Id);
        state.DocumentVersion.Should().Be(harness.Source.Version);
        state.Actions.Should().Contain(action => action.Code == "post");
        harness.Uow.Verify(unit => unit.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static PermissionSnapshot Snapshot(params NgbPermissionKey[] permissions)
        => new(
            Guid.NewGuid(),
            "subject",
            true,
            true,
            false,
            1,
            new HashSet<NgbPermissionKey>(permissions));
}
