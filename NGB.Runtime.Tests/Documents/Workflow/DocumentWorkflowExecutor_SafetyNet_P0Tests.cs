using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents.Workflow;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.Workflow;

public sealed class DocumentWorkflowExecutor_SafetyNet_P0Tests
{
    [Fact]
    public async Task InterfaceDocumentIdOverload_ForwardsEveryArgumentToCanonicalOverload()
    {
        IDocumentWorkflowExecutor executor = new CapturingWorkflowExecutor();
        var documentId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        Func<CancellationToken, Task<bool>> action = _ => Task.FromResult(true);

        await executor.ExecuteAsync(documentId, "operation", action, manageTransaction: false, cancellation.Token);

        var capturing = (CapturingWorkflowExecutor)executor;
        capturing.OperationName.Should().Be("operation");
        capturing.DocumentId.Should().Be(documentId);
        capturing.Action.Should().BeSameAs(action);
        capturing.ManageTransaction.Should().BeFalse();
        capturing.CancellationToken.Should().Be(cancellation.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WhenOperationNameIsBlank_ThrowsArgumentRequired(string? operationName)
    {
        var sut = Create();

        var action = () => sut.ExecuteAsync(operationName!, null, _ => Task.FromResult(true));

        var exception = await action.Should().ThrowAsync<NgbArgumentRequiredException>();
        exception.Which.ParamName.Should().Be("operationName");
    }

    [Fact]
    public async Task ExecuteAsync_WhenActionIsNull_ThrowsArgumentRequired()
    {
        var sut = Create();

        var action = () => sut.ExecuteAsync("operation", null, null!);

        var exception = await action.Should().ThrowAsync<NgbArgumentRequiredException>();
        exception.Which.ParamName.Should().Be("action");
    }

    [Fact]
    public async Task ExecuteAsync_WhenActionThrowsNonNgbException_WrapsIntoNgbUnexpectedException()
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.EnsureActiveTransaction());

        var documentId = Guid.NewGuid();

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logger = Mock.Of<ILogger<DocumentWorkflowExecutor>>();

        var sut = new DocumentWorkflowExecutor(uow.Object, locks.Object, logger);

        Func<Task> act = () => sut.ExecuteAsync(
            operationName: "it.doc.workflow",
            documentId: documentId,
            action: _ => throw new NullReferenceException("boom"),
            manageTransaction: false,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbUnexpectedException>();

        ex.Which.ErrorCode.Should().Be(NgbUnexpectedException.Code);
        ex.Which.Context.Should().ContainKey("operation").WhoseValue.Should().Be("it.doc.workflow");
        ex.Which.Context.Should().ContainKey("documentId").WhoseValue.Should().Be(documentId);
        ex.Which.Context.Should().ContainKey("exceptionType");
        ex.Which.InnerException.Should().BeOfType<NullReferenceException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WhenActionCompletes_HandlesCompletedAndNoOpOutcomes(bool didWork)
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.EnsureActiveTransaction());
        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        var documentId = didWork ? Guid.NewGuid() : (Guid?)null;
        if (documentId.HasValue)
        {
            locks.Setup(x => x.LockDocumentAsync(documentId.Value, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        var sut = new DocumentWorkflowExecutor(
            uow.Object,
            locks.Object,
            Mock.Of<ILogger<DocumentWorkflowExecutor>>());

        await sut.ExecuteAsync("operation", documentId, _ => Task.FromResult(didWork), manageTransaction: false);

        uow.Verify(x => x.EnsureActiveTransaction(), Times.Once);
        if (documentId.HasValue)
            locks.Verify(x => x.LockDocumentAsync(documentId.Value, It.IsAny<CancellationToken>()), Times.Once);
        else
            locks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenActionIsCanceled_RethrowsCancellationWithoutWrapping()
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.EnsureActiveTransaction());
        var sut = new DocumentWorkflowExecutor(
            uow.Object,
            Mock.Of<IAdvisoryLockManager>(),
            Mock.Of<ILogger<DocumentWorkflowExecutor>>());
        var expected = new OperationCanceledException("cancelled");

        var action = () => sut.ExecuteAsync(
            "operation",
            documentId: null,
            _ => throw expected,
            manageTransaction: false);

        var thrown = await action.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().BeSameAs(expected);
    }

    private static DocumentWorkflowExecutor Create()
        => new(
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IAdvisoryLockManager>(),
            Mock.Of<ILogger<DocumentWorkflowExecutor>>());

    private sealed class CapturingWorkflowExecutor : IDocumentWorkflowExecutor
    {
        public string? OperationName { get; private set; }
        public Guid? DocumentId { get; private set; }
        public Func<CancellationToken, Task<bool>>? Action { get; private set; }
        public bool ManageTransaction { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task ExecuteAsync(
            string operationName,
            Guid? documentId,
            Func<CancellationToken, Task<bool>> action,
            bool manageTransaction = true,
            CancellationToken ct = default)
        {
            OperationName = operationName;
            DocumentId = documentId;
            Action = action;
            ManageTransaction = manageTransaction;
            CancellationToken = ct;
            return Task.CompletedTask;
        }
    }
}
