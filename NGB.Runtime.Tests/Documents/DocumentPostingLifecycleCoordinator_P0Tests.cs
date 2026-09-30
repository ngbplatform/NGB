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
    public async Task ExecuteAccountingAsync_WhenDelegateIsNull_ThrowsArgumentRequired_WithCustomClock()
    {
        var sut = new DocumentPostingLifecycleCoordinator(
            Mock.Of<IDocumentOperationStateRepository>(),
            Mock.Of<IPostingStateRepository>(),
            Mock.Of<IOperationalRegisterWriteStateRepository>(),
            Mock.Of<IReferenceRegisterWriteStateRepository>(),
            new FixedTimeProvider());

        var action = () => sut.ExecuteAccountingAsync(
            Guid.CreateVersion7(),
            PostingOperation.Post,
            null!,
            default);

        var exception = await action.Should().ThrowAsync<NgbArgumentRequiredException>();
        exception.Which.ParamName.Should().Be("execute");
    }

    [Fact]
    public async Task ExecuteAccountingAsync_WhenAccountingExecuted_CompletesNormally()
    {
        var sut = CreateStrictSut();

        await sut.ExecuteAccountingAsync(
            Guid.CreateVersion7(),
            PostingOperation.Post,
            () => Task.FromResult(PostingResult.Executed),
            CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAccountingAsync_WhenAccountingReturnsUnknownResult_CompletesNormally()
    {
        var sut = CreateStrictSut();

        await sut.ExecuteAccountingAsync(
            Guid.CreateVersion7(),
            PostingOperation.Post,
            () => Task.FromResult((PostingResult)int.MaxValue),
            CancellationToken.None);
    }

    [Fact]
    public async Task CancelAsync_ForwardsOperationAndCancellationToken()
    {
        var documentId = Guid.CreateVersion7();
        using var cancellation = new CancellationTokenSource();
        var docState = new Mock<IDocumentOperationStateRepository>(MockBehavior.Strict);
        docState.Setup(x => x.ClearInProgressStateAsync(documentId, PostingOperation.Repost, cancellation.Token))
            .Returns(Task.CompletedTask);
        var sut = CreateStrictSut(docState.Object);

        await sut.CancelAsync(documentId, PostingOperation.Repost, cancellation.Token);

        docState.VerifyAll();
    }

    [Fact]
    public async Task CompleteSuccessfulTransitionAsync_WhenPost_MarksCompleted_AndRearmsUnpostState()
    {
        var documentId = Guid.CreateVersion7();
        var docState = new Mock<IDocumentOperationStateRepository>(MockBehavior.Strict);
        docState.Setup(x => x.MarkCompletedAsync(documentId, PostingOperation.Post, It.IsAny<DateTime>(), default))
            .Returns(Task.CompletedTask);
        docState.Setup(x => x.ClearCompletedStateAsync(documentId, PostingOperation.Unpost, default))
            .Returns(Task.CompletedTask);

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        postingLog.Setup(x => x.ClearCompletedStateAsync(documentId, PostingOperation.Unpost, default))
            .Returns(Task.CompletedTask);
        var opreg = new Mock<IOperationalRegisterWriteStateRepository>(MockBehavior.Strict);
        opreg.Setup(x => x.ClearCompletedStateByDocumentAsync(documentId, OperationalRegisterWriteOperation.Unpost, default))
            .Returns(Task.CompletedTask);
        var refreg = new Mock<IReferenceRegisterWriteStateRepository>(MockBehavior.Strict);
        refreg.Setup(x => x.ClearCompletedStateByDocumentAsync(documentId, ReferenceRegisterWriteOperation.Unpost, default))
            .Returns(Task.CompletedTask);
        var sut = new DocumentPostingLifecycleCoordinator(
            docState.Object,
            postingLog.Object,
            opreg.Object,
            refreg.Object,
            new FixedTimeProvider());

        await sut.CompleteSuccessfulTransitionAsync(documentId, PostingOperation.Post, default);

        docState.VerifyAll();
        postingLog.VerifyAll();
        opreg.VerifyAll();
        refreg.VerifyAll();
    }

    [Theory]
    [InlineData(PostingOperation.Repost)]
    [InlineData((PostingOperation)short.MaxValue)]
    public async Task CompleteSuccessfulTransitionAsync_WhenOperationHasNoOppositeState_OnlyMarksCompleted(
        PostingOperation operation)
    {
        var documentId = Guid.CreateVersion7();
        var docState = new Mock<IDocumentOperationStateRepository>(MockBehavior.Strict);
        docState.Setup(x => x.MarkCompletedAsync(documentId, operation, It.IsAny<DateTime>(), default))
            .Returns(Task.CompletedTask);
        var sut = CreateStrictSut(docState.Object);

        await sut.CompleteSuccessfulTransitionAsync(documentId, operation, default);

        docState.VerifyAll();
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

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static DocumentPostingLifecycleCoordinator CreateStrictSut(
        IDocumentOperationStateRepository? documentOperationStateRepository = null)
        => new(
            documentOperationStateRepository ?? new Mock<IDocumentOperationStateRepository>(MockBehavior.Strict).Object,
            new Mock<IPostingStateRepository>(MockBehavior.Strict).Object,
            new Mock<IOperationalRegisterWriteStateRepository>(MockBehavior.Strict).Object,
            new Mock<IReferenceRegisterWriteStateRepository>(MockBehavior.Strict).Object,
            new FixedTimeProvider());
}
