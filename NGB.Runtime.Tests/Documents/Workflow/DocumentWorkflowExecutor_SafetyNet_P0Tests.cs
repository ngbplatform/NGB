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
}
