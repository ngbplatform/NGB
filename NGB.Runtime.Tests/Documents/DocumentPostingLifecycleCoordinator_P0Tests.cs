using FluentAssertions;
using Moq;
using NGB.Accounting.PostingState;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.PostingState;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentPostingLifecycleCoordinator_P0Tests
{
    [Fact]
    public async Task BeginAsync_WhenDocumentStateBegun_ReturnsNormally_AndDoesNotTouchSubsystemState()
    {
        var documentId = Guid.CreateVersion7();

        var docState = new Mock<IDocumentOperationStateRepository>(MockBehavior.Strict);
        docState.Setup(x => x.TryBeginAsync(documentId, PostingOperation.Post, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.Begun);

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        var opreg = new Mock<IOperationalRegisterWriteStateRepository>(MockBehavior.Strict);
        var refreg = new Mock<IReferenceRegisterWriteStateRepository>(MockBehavior.Strict);

        var sut = new DocumentPostingLifecycleCoordinator(docState.Object, postingLog.Object, opreg.Object, refreg.Object);

        var result = await sut.BeginAsync(documentId, PostingOperation.Post, CancellationToken.None);
        result.Should().Be(DocumentLifecycleBeginResult.Begun);
    }

    [Fact]
    public async Task BeginAsync_WhenDocumentStateAlreadyCompleted_ForPost_ThrowsInvariantConflict()
    {
        var documentId = Guid.CreateVersion7();

        var docState = new Mock<IDocumentOperationStateRepository>(MockBehavior.Strict);
        docState.Setup(x => x.TryBeginAsync(documentId, PostingOperation.Post, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.AlreadyCompleted);

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        var opreg = new Mock<IOperationalRegisterWriteStateRepository>(MockBehavior.Strict);
        var refreg = new Mock<IReferenceRegisterWriteStateRepository>(MockBehavior.Strict);

        var sut = new DocumentPostingLifecycleCoordinator(docState.Object, postingLog.Object, opreg.Object, refreg.Object);

        var act = () => sut.BeginAsync(documentId, PostingOperation.Post, CancellationToken.None);
        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*inconsistent*");
    }

    [Fact]
    public async Task BeginAsync_WhenDocumentStateAlreadyCompleted_ForRepost_ReturnsNoOp()
    {
        var documentId = Guid.CreateVersion7();

        var docState = new Mock<IDocumentOperationStateRepository>(MockBehavior.Strict);
        docState.Setup(x => x.TryBeginAsync(documentId, PostingOperation.Repost, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.AlreadyCompleted);

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        var opreg = new Mock<IOperationalRegisterWriteStateRepository>(MockBehavior.Strict);
        var refreg = new Mock<IReferenceRegisterWriteStateRepository>(MockBehavior.Strict);

        var sut = new DocumentPostingLifecycleCoordinator(docState.Object, postingLog.Object, opreg.Object, refreg.Object);

        var result = await sut.BeginAsync(documentId, PostingOperation.Repost, CancellationToken.None);
        result.Should().Be(DocumentLifecycleBeginResult.NoOp);
    }

    [Fact]
    public async Task BeginAsync_WhenDocumentStateInProgress_ThrowsConflict()
    {
        var documentId = Guid.CreateVersion7();

        var docState = new Mock<IDocumentOperationStateRepository>(MockBehavior.Strict);
        docState.Setup(x => x.TryBeginAsync(documentId, PostingOperation.Unpost, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.InProgress);

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        var opreg = new Mock<IOperationalRegisterWriteStateRepository>(MockBehavior.Strict);
        var refreg = new Mock<IReferenceRegisterWriteStateRepository>(MockBehavior.Strict);

        var sut = new DocumentPostingLifecycleCoordinator(docState.Object, postingLog.Object, opreg.Object, refreg.Object);

        var act = () => sut.BeginAsync(documentId, PostingOperation.Unpost, CancellationToken.None);
        await act.Should().ThrowAsync<PostingAlreadyInProgressException>();
    }

    [Fact]
    public async Task ExecuteAccountingAsync_WhenAccountingAlreadyCompleted_ThrowsInvariantConflict()
    {
        var documentId = Guid.CreateVersion7();

        var docState = new Mock<IDocumentOperationStateRepository>(MockBehavior.Strict);
        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        var opreg = new Mock<IOperationalRegisterWriteStateRepository>(MockBehavior.Strict);
        var refreg = new Mock<IReferenceRegisterWriteStateRepository>(MockBehavior.Strict);

        var sut = new DocumentPostingLifecycleCoordinator(docState.Object, postingLog.Object, opreg.Object, refreg.Object);

        Task<PostingResult> Execute() => Task.FromResult(PostingResult.AlreadyCompleted);

        var act = () => sut.ExecuteAccountingAsync(documentId, PostingOperation.Repost, Execute, CancellationToken.None);
        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*inconsistent*");
    }

    [Fact]
    public async Task CompleteSuccessfulTransitionAsync_WhenUnpost_MarksCompleted_AndRearmsOppositeDocumentAndSubsystemState()
    {
        var documentId = Guid.CreateVersion7();

        var docState = new Mock<IDocumentOperationStateRepository>(MockBehavior.Strict);
        docState.Setup(x => x.MarkCompletedAsync(documentId, PostingOperation.Unpost, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        docState.Setup(x => x.ClearCompletedStateAsync(documentId, PostingOperation.Post, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        docState.Setup(x => x.ClearCompletedStateAsync(documentId, PostingOperation.Repost, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        postingLog.Setup(x => x.ClearCompletedStateAsync(documentId, PostingOperation.Post, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        postingLog.Setup(x => x.ClearCompletedStateAsync(documentId, PostingOperation.Repost, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var opreg = new Mock<IOperationalRegisterWriteStateRepository>(MockBehavior.Strict);
        opreg.Setup(x => x.ClearCompletedStateByDocumentAsync(documentId, OperationalRegisterWriteOperation.Post, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        opreg.Setup(x => x.ClearCompletedStateByDocumentAsync(documentId, OperationalRegisterWriteOperation.Repost, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var refreg = new Mock<IReferenceRegisterWriteStateRepository>(MockBehavior.Strict);
        refreg.Setup(x => x.ClearCompletedStateByDocumentAsync(documentId, ReferenceRegisterWriteOperation.Post, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        refreg.Setup(x => x.ClearCompletedStateByDocumentAsync(documentId, ReferenceRegisterWriteOperation.Repost, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new DocumentPostingLifecycleCoordinator(docState.Object, postingLog.Object, opreg.Object, refreg.Object);

        await sut.CompleteSuccessfulTransitionAsync(documentId, PostingOperation.Unpost, CancellationToken.None);
    }
}
