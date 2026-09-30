using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.PostingState;
using NGB.Accounting.Registers;
using NGB.Core.Dimensions;
using NGB.Persistence.Locks;
using NGB.Persistence.Periods;
using NGB.Persistence.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Posting;

public sealed class RepostingAndUnpostingServiceFullCoverageTests
{
    [Fact]
    public async Task Repost_WhenNoOldEntries_RollsBackAndThrowsInvalidArgument()
    {
        var fixture = new Fixture();
        fixture.Reader.Setup(x => x.GetByDocumentAsync(fixture.DocumentId, fixture.Token)).ReturnsAsync([]);

        var action = () => fixture.Reposting.RepostAsync(
            fixture.DocumentId,
            (_, _) => Task.CompletedTask,
            fixture.Token);

        var exception = await action.Should().ThrowAsync<NgbArgumentInvalidException>();
        exception.Which.ParamName.Should().Be("documentId");
        fixture.Uow.Verify(x => x.BeginTransactionAsync(fixture.Token), Times.Once);
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Locks.Verify(x => x.LockDocumentAsync(fixture.DocumentId, fixture.Token), Times.Once);
    }

    [Fact]
    public async Task Repost_WhenOldEntriesExist_PostsStornoThenNewEntriesInsideOneTransaction()
    {
        var fixture = new Fixture();
        var oldEntry = fixture.CreateOldEntry();
        fixture.Reader.Setup(x => x.GetByDocumentAsync(fixture.DocumentId, fixture.Token))
            .ReturnsAsync([oldEntry]);
        fixture.ArrangeEngineAlreadyCompleted(PostingOperation.Repost);
        CancellationToken callbackToken = default;

        await fixture.Reposting.RepostAsync(
            fixture.DocumentId,
            (context, ct) =>
            {
                callbackToken = ct;
                context.Entries.Should().ContainSingle();
                var storno = context.Entries[0];
                storno.DocumentId.Should().Be(oldEntry.DocumentId);
                storno.Period.Should().Be(oldEntry.Period);
                storno.Debit.Should().BeSameAs(oldEntry.Credit);
                storno.Credit.Should().BeSameAs(oldEntry.Debit);
                storno.Amount.Should().Be(oldEntry.Amount);
                storno.DebitDimensions.Should().BeSameAs(oldEntry.CreditDimensions);
                storno.CreditDimensions.Should().BeSameAs(oldEntry.DebitDimensions);
                storno.IsStorno.Should().BeTrue();
                context.Post(
                    fixture.DocumentId,
                    fixture.PeriodUtc.AddDays(1),
                    oldEntry.Debit,
                    oldEntry.Credit,
                    25m);
                return Task.CompletedTask;
            },
            fixture.Token);

        callbackToken.Should().Be(fixture.Token);
        fixture.Context.Entries.Should().HaveCount(2);
        fixture.Uow.Verify(x => x.CommitAsync(fixture.Token), Times.Once);
        fixture.Uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Locks.Verify(x => x.LockDocumentAsync(fixture.DocumentId, fixture.Token), Times.Exactly(2));
    }

    [Fact]
    public async Task Repost_WhenNewPostingFails_RollsBackAndPreservesOriginalException()
    {
        var fixture = new Fixture();
        fixture.Reader.Setup(x => x.GetByDocumentAsync(fixture.DocumentId, fixture.Token))
            .ReturnsAsync([fixture.CreateOldEntry()]);
        fixture.ContextFactory.Setup(x => x.CreateAsync(fixture.Token)).ReturnsAsync(fixture.Context);
        var original = new TestPostingException();

        var action = () => fixture.Reposting.RepostAsync(
            fixture.DocumentId,
            (_, ct) => Task.FromException(ct == fixture.Token ? original : new InvalidOperationException()),
            fixture.Token);

        var exception = await action.Should().ThrowAsync<TestPostingException>();
        exception.Which.Should().BeSameAs(original);
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unpost_WhenNoOldEntries_CommitsWithoutInvokingPostingEngine()
    {
        var fixture = new Fixture();
        fixture.Reader.Setup(x => x.GetByDocumentAsync(fixture.DocumentId, fixture.Token)).ReturnsAsync([]);

        await fixture.Unposting.UnpostAsync(fixture.DocumentId, fixture.Token);

        fixture.ContextFactory.Verify(x => x.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Uow.Verify(x => x.CommitAsync(fixture.Token), Times.Once);
        fixture.Uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Locks.Verify(x => x.LockDocumentAsync(fixture.DocumentId, fixture.Token), Times.Once);
    }

    [Fact]
    public async Task Unpost_WhenOldEntriesExist_PostsStornoInsideOuterTransaction()
    {
        var fixture = new Fixture();
        var oldEntry = fixture.CreateOldEntry();
        fixture.Reader.Setup(x => x.GetByDocumentAsync(fixture.DocumentId, fixture.Token))
            .ReturnsAsync([oldEntry]);
        fixture.ArrangeEngineAlreadyCompleted(PostingOperation.Unpost);

        await fixture.Unposting.UnpostAsync(fixture.DocumentId, fixture.Token);

        fixture.Context.Entries.Should().ContainSingle();
        var storno = fixture.Context.Entries[0];
        storno.DocumentId.Should().Be(oldEntry.DocumentId);
        storno.Period.Should().Be(oldEntry.Period);
        storno.Debit.Should().BeSameAs(oldEntry.Credit);
        storno.Credit.Should().BeSameAs(oldEntry.Debit);
        storno.Amount.Should().Be(oldEntry.Amount);
        storno.DebitDimensions.Should().BeSameAs(oldEntry.CreditDimensions);
        storno.CreditDimensions.Should().BeSameAs(oldEntry.DebitDimensions);
        storno.IsStorno.Should().BeTrue();
        fixture.Uow.Verify(x => x.CommitAsync(fixture.Token), Times.Once);
        fixture.Uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Locks.Verify(x => x.LockDocumentAsync(fixture.DocumentId, fixture.Token), Times.Exactly(2));
    }

    private sealed class Fixture
    {
        private bool _transactionActive;

        public Guid DocumentId { get; } = Guid.CreateVersion7();
        public DateTime PeriodUtc { get; } = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        public CancellationToken Token { get; } = new CancellationTokenSource().Token;
        public Mock<IAccountingEntryReader> Reader { get; } = new(MockBehavior.Strict);
        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Strict);
        public Mock<IAdvisoryLockManager> Locks { get; } = new(MockBehavior.Strict);
        public Mock<IAccountingPostingContextFactory> ContextFactory { get; } = new(MockBehavior.Strict);
        public Mock<IPostingStateRepository> PostingState { get; } = new(MockBehavior.Strict);
        public AccountingPostingContext Context { get; }
        public RepostingService Reposting { get; }
        public UnpostingService Unposting { get; }

        public Fixture()
        {
            var chart = new ChartOfAccounts();
            Context = new AccountingPostingContext(chart);

            Uow.SetupGet(x => x.HasActiveTransaction).Returns(() => _transactionActive);
            Uow.Setup(x => x.BeginTransactionAsync(Token))
                .Callback(() => _transactionActive = true)
                .Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(Token))
                .Callback(() => _transactionActive = false)
                .Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(Token))
                .Callback(() => _transactionActive = false)
                .Returns(Task.CompletedTask);
            Locks.Setup(x => x.LockDocumentAsync(DocumentId, Token)).Returns(Task.CompletedTask);

            var engine = new PostingEngine(
                ContextFactory.Object,
                Uow.Object,
                Locks.Object,
                new Mock<IAccountingEntryWriter>(MockBehavior.Strict).Object,
                new Mock<IAccountingTurnoverWriter>(MockBehavior.Strict).Object,
                new Mock<IDimensionSetService>(MockBehavior.Strict).Object,
                new Mock<IAccountingOperationalBalanceReader>(MockBehavior.Strict).Object,
                new Mock<IClosedPeriodRepository>(MockBehavior.Strict).Object,
                new Mock<IAccountingPostingValidator>(MockBehavior.Strict).Object,
                PostingState.Object,
                new Mock<ILogger<PostingEngine>>().Object);

            Reposting = new RepostingService(engine, Reader.Object, Uow.Object, Locks.Object);
            Unposting = new UnpostingService(engine, Reader.Object, Uow.Object, Locks.Object);
        }

        public void ArrangeEngineAlreadyCompleted(PostingOperation operation)
        {
            ContextFactory.Setup(x => x.CreateAsync(Token)).ReturnsAsync(Context);
            PostingState.Setup(x => x.TryBeginAsync(DocumentId, operation, It.IsAny<DateTime>(), Token))
                .ReturnsAsync(PostingStateBeginResult.AlreadyCompleted);
        }

        public AccountingEntry CreateOldEntry()
        {
            var debit = new Account(null, "1000", "Debit", AccountType.Liability, StatementSection.Liabilities);
            var credit = new Account(null, "2000", "Credit", AccountType.Equity, StatementSection.Equity);
            var debitDimensions = new DimensionBag([
                new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7())
            ]);
            var creditDimensions = new DimensionBag([
                new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7())
            ]);

            return new AccountingEntry
            {
                DocumentId = DocumentId,
                Period = PeriodUtc,
                Debit = debit,
                Credit = credit,
                Amount = 125.50m,
                DebitDimensions = debitDimensions,
                CreditDimensions = creditDimensions
            };
        }
    }

    private sealed class TestPostingException : Exception
    {
    }
}
