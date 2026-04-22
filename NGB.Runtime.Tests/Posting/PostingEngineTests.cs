using FluentAssertions;
using Moq;
using NGB.Accounting.Posting;
using NGB.Accounting.PostingState;
using NGB.Persistence.Locks;
using NGB.Persistence.Periods;
using NGB.Persistence.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Posting;

public sealed class PostingEngineTests
{
    [Fact]
    public async Task PostAsync_WhenPostingActionProducesNoEntries_Throws()
    {
        // Arrange
        var context = new Mock<IAccountingPostingContext>(MockBehavior.Strict);
        context.SetupGet(x => x.Entries).Returns([]);

        var contextFactory = new Mock<IAccountingPostingContextFactory>(MockBehavior.Strict);
        contextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(context.Object);

        var dimensionSetService = new Mock<NGB.Runtime.Dimensions.IDimensionSetService>(MockBehavior.Loose);
        dimensionSetService
            .Setup(x => x.GetOrCreateIdAsync(It.IsAny<NGB.Core.Dimensions.DimensionBag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Empty);

        var engine = new PostingEngine(
            contextFactory: contextFactory.Object,
            uow: Mock.Of<IUnitOfWork>(),
            advisoryLocks: Mock.Of<IAdvisoryLockManager>(),
            entryWriter: Mock.Of<IAccountingEntryWriter>(),
            turnoverWriter: Mock.Of<IAccountingTurnoverWriter>(),
            dimensionSetService: dimensionSetService.Object,
            operationalBalanceReader: Mock.Of<IAccountingOperationalBalanceReader>(),
            closedPeriodRepository: Mock.Of<IClosedPeriodRepository>(),
            validator: Mock.Of<NGB.Accounting.Posting.Validators.IAccountingPostingValidator>(),
            postingLog: Mock.Of<IPostingStateRepository>(),
            logger: Mock.Of<Microsoft.Extensions.Logging.ILogger<PostingEngine>>());

        // Act
        Func<Task> act = () => engine.PostAsync(
            operation: PostingOperation.Post,
            postingAction: (_, _) => Task.CompletedTask,
            manageTransaction: true);

        // Assert
        (await act.Should().ThrowAsync<NgbInvariantViolationException>())
            .Which.Message.Should().Contain("requires at least one accounting entry");
    }
}
