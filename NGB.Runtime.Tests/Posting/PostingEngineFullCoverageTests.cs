using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Posting;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.PostingState;
using NGB.Accounting.Registers;
using NGB.Accounting.Turnovers;
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

public sealed class PostingEngineFullCoverageTests
{
    private static readonly DateTime MarchUtc = new(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PostAsync_BooleanOverload_DelegatesToValidatedPipeline()
    {
        var fixture = new Fixture();

        var act = () => fixture.Sut.PostAsync(
            (_, _) => Task.CompletedTask,
            manageTransaction: false,
            fixture.Token);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*requires at least one accounting entry*");
        fixture.Uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PostAsync_WhenDocumentIdIsEmpty_RollsBackBeforeStartingPostingState()
    {
        var fixture = new Fixture();
        var debit = CreateAccount("1010", NegativeBalancePolicy.Allow);
        var credit = CreateAccount("2010", NegativeBalancePolicy.Allow);

        var act = () => fixture.Sut.PostAsync(
            PostingOperation.Post,
            (context, _) =>
            {
                context.Post(Guid.Empty, MarchUtc, debit, credit, 1m);
                return Task.CompletedTask;
            },
            manageTransaction: true,
            fixture.Token);

        var exception = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        exception.Which.ParamName.Should().Be("documentId");
        fixture.PostingState.Verify(
            x => x.TryBeginAsync(
                It.IsAny<Guid>(),
                It.IsAny<PostingOperation>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Uow.Verify(x => x.RollbackAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task PostAsync_ChecksAllDistinctPeriodsWithOneBatchRead_AndRejectsFirstClosed()
    {
        var fixture = new Fixture();
        var documentId = Guid.CreateVersion7();
        var debit = CreateAccount("1010", NegativeBalancePolicy.Allow);
        var credit = CreateAccount("2010", NegativeBalancePolicy.Allow);
        var march = new DateOnly(2026, 3, 1);
        var april = new DateOnly(2026, 4, 1);
        fixture.ClosedPeriods
            .Setup(x => x.FindFirstClosedAsync(
                It.Is<IReadOnlyCollection<DateOnly>>(periods =>
                    periods.SequenceEqual(new[] { march, april })),
                fixture.Token))
            .ReturnsAsync(march);

        var act = () => fixture.Sut.PostAsync(
            PostingOperation.Post,
            (context, _) =>
            {
                context.Post(documentId, new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc), debit, credit, 1m);
                context.Post(documentId, MarchUtc, debit, credit, 1m);
                return Task.CompletedTask;
            },
            manageTransaction: true,
            fixture.Token);

        var exception = await act.Should().ThrowAsync<PostingPeriodClosedException>();
        exception.Which.Period.Should().Be(march);
        fixture.ClosedPeriods.Verify(x => x.FindFirstClosedAsync(
            It.IsAny<IReadOnlyCollection<DateOnly>>(), fixture.Token), Times.Once);
        fixture.EntryWriter.Verify(
            x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingEntry>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostAsync_WhenValidatorIllegallyRemovesEntries_RejectsTheMutatedContext()
    {
        var fixture = new Fixture();
        var documentId = Guid.CreateVersion7();
        var debit = CreateAccount("1010", NegativeBalancePolicy.Allow);
        var credit = CreateAccount("2010", NegativeBalancePolicy.Allow);
        fixture.Validator
            .Setup(x => x.Validate(It.IsAny<IReadOnlyList<AccountingEntry>>()))
            .Callback(fixture.Context.Clear);

        var act = () => fixture.Sut.PostAsync(
            PostingOperation.Post,
            (context, _) =>
            {
                context.Post(documentId, MarchUtc, debit, credit, 1m);
                return Task.CompletedTask;
            },
            manageTransaction: true,
            fixture.Token);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Posting produced zero entries*");
        fixture.EntryWriter.Verify(
            x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingEntry>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Uow.Verify(x => x.RollbackAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task PostAsync_WhenDimensionBatchResolverReturnsNull_RejectsTheBrokenContract()
    {
        var fixture = new Fixture();
        var documentId = Guid.CreateVersion7();
        var debit = CreateAccount("1010", NegativeBalancePolicy.Allow);
        var credit = CreateAccount("2010", NegativeBalancePolicy.Allow);
        fixture.DimensionSets
            .Setup(x => x.GetOrCreateIdsAsync(It.IsAny<IReadOnlyList<DimensionBag>>(), fixture.Token))
            .ReturnsAsync((IReadOnlyList<Guid>)null!);

        var act = () => fixture.Sut.PostAsync(
            PostingOperation.Post,
            (context, _) =>
            {
                context.Post(documentId, MarchUtc, debit, credit, 1m);
                return Task.CompletedTask;
            },
            manageTransaction: true,
            fixture.Token);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*returned 0 id(s) for 2 bag(s)*");
        fixture.Uow.Verify(x => x.RollbackAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task PostAsync_WhenDimensionBatchResolverReturnsWrongCount_RejectsTheBrokenContract()
    {
        var fixture = new Fixture();
        var documentId = Guid.CreateVersion7();
        var debit = CreateAccount("1010", NegativeBalancePolicy.Allow);
        var credit = CreateAccount("2010", NegativeBalancePolicy.Allow);
        fixture.DimensionSets
            .Setup(x => x.GetOrCreateIdsAsync(It.IsAny<IReadOnlyList<DimensionBag>>(), fixture.Token))
            .ReturnsAsync([Guid.CreateVersion7()]);

        var act = () => fixture.Sut.PostAsync(
            PostingOperation.Post,
            (context, _) =>
            {
                context.Post(documentId, MarchUtc, debit, credit, 1m);
                return Task.CompletedTask;
            },
            manageTransaction: true,
            fixture.Token);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*returned 1 id(s) for 2 bag(s)*");
        fixture.Uow.Verify(x => x.RollbackAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task PostAsync_WhenOperationalReaderReturnsDuplicateKeys_RejectsWithDiagnosticContext()
    {
        var fixture = new Fixture();
        var documentId = Guid.CreateVersion7();
        var debit = CreateAccount("1010", NegativeBalancePolicy.Warn);
        var credit = CreateAccount("1020", NegativeBalancePolicy.Warn);
        var march = new DateOnly(2026, 3, 1);
        var april = new DateOnly(2026, 4, 1);
        fixture.OperationalBalances
            .Setup(x => x.GetForKeysAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<AccountingBalanceKey>>(),
                fixture.Token))
            .ReturnsAsync((DateOnly period, IReadOnlyList<AccountingBalanceKey> _, CancellationToken _) =>
                period == march
                    ?
                    [
                        new AccountingOperationalBalanceSnapshot
                        {
                            Period = march,
                            AccountId = debit.Id,
                            DimensionSetId = Guid.Empty
                        },
                        new AccountingOperationalBalanceSnapshot
                        {
                            Period = march,
                            AccountId = debit.Id,
                            DimensionSetId = Guid.Empty
                        }
                    ]
                    : []);

        var act = () => fixture.Sut.PostAsync(
            PostingOperation.Post,
            (context, _) =>
            {
                context.Post(documentId, new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc), debit, credit, 1m);
                context.Post(documentId, MarchUtc, debit, credit, 1m);
                return Task.CompletedTask;
            },
            manageTransaction: true,
            fixture.Token);

        var exception = await act.Should().ThrowAsync<NgbInvariantViolationException>();
        exception.Which.Message.Should().Contain("duplicate keys");
        exception.Which.Context.Should().Contain(new Dictionary<string, object?>
        {
            ["period"] = "2026-03-01",
            ["accountId"] = debit.Id,
            ["dimensionSetId"] = Guid.Empty
        });
        fixture.AdvisoryLocks.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IAdvisoryLockManager.LockPeriodAsync))
            .Select(invocation => (DateOnly)invocation.Arguments[0])
            .Should().ContainInOrder(march, april);
        fixture.Uow.Verify(x => x.RollbackAsync(CancellationToken.None), Times.Once);
    }

    private static Account CreateAccount(string code, NegativeBalancePolicy policy)
        => new(
            Guid.CreateVersion7(),
            code,
            $"Account {code}",
            AccountType.Asset,
            negativeBalancePolicy: policy);

    private sealed class Fixture
    {
        public Fixture()
        {
            ContextFactory.Setup(x => x.CreateAsync(Token)).ReturnsAsync(Context);
            Uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
            Uow.Setup(x => x.BeginTransactionAsync(Token)).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(CancellationToken.None)).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(CancellationToken.None)).Returns(Task.CompletedTask);
            AdvisoryLocks
                .Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), Token))
                .Returns(Task.CompletedTask);
            AdvisoryLocks
                .Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), Token))
                .Returns(Task.CompletedTask);
            EntryWriter
                .Setup(x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingEntry>>(), Token))
                .Returns(Task.CompletedTask);
            TurnoverWriter
                .Setup(x => x.WriteAsync(It.IsAny<IEnumerable<AccountingTurnover>>(), Token))
                .Returns(Task.CompletedTask);
            DimensionSets
                .Setup(x => x.GetOrCreateIdsAsync(It.IsAny<IReadOnlyList<DimensionBag>>(), Token))
                .ReturnsAsync((IReadOnlyList<DimensionBag> bags, CancellationToken _) =>
                    bags.Select(static _ => Guid.Empty).ToArray());
            ClosedPeriods
                .Setup(x => x.FindFirstClosedAsync(It.IsAny<IReadOnlyCollection<DateOnly>>(), Token))
                .ReturnsAsync((DateOnly?)null);
            Validator.Setup(x => x.Validate(It.IsAny<IReadOnlyList<AccountingEntry>>()));
            PostingState
                .Setup(x => x.TryBeginAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<PostingOperation>(),
                    It.IsAny<DateTime>(),
                    Token))
                .ReturnsAsync(PostingStateBeginResult.Begun);
            PostingState
                .Setup(x => x.MarkCompletedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<PostingOperation>(),
                    It.IsAny<DateTime>(),
                    Token))
                .Returns(Task.CompletedTask);
            OperationalBalances
                .Setup(x => x.GetForKeysAsync(
                    It.IsAny<DateOnly>(),
                    It.IsAny<IReadOnlyList<AccountingBalanceKey>>(),
                    Token))
                .ReturnsAsync(Array.Empty<AccountingOperationalBalanceSnapshot>());

            Sut = new PostingEngine(
                ContextFactory.Object,
                Uow.Object,
                AdvisoryLocks.Object,
                EntryWriter.Object,
                TurnoverWriter.Object,
                DimensionSets.Object,
                OperationalBalances.Object,
                ClosedPeriods.Object,
                Validator.Object,
                PostingState.Object,
                Logger.Object);
        }

        public CancellationToken Token { get; } = new CancellationTokenSource().Token;
        public MutablePostingContext Context { get; } = new();
        public Mock<IAccountingPostingContextFactory> ContextFactory { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IAdvisoryLockManager> AdvisoryLocks { get; } = new();
        public Mock<IAccountingEntryWriter> EntryWriter { get; } = new();
        public Mock<IAccountingTurnoverWriter> TurnoverWriter { get; } = new();
        public Mock<IDimensionSetService> DimensionSets { get; } = new();
        public Mock<IAccountingOperationalBalanceReader> OperationalBalances { get; } = new();
        public Mock<IClosedPeriodRepository> ClosedPeriods { get; } = new();
        public Mock<IAccountingPostingValidator> Validator { get; } = new();
        public Mock<IPostingStateRepository> PostingState { get; } = new();
        public Mock<ILogger<PostingEngine>> Logger { get; } = new();
        public PostingEngine Sut { get; }
    }

    private sealed class MutablePostingContext : IAccountingPostingContext
    {
        private readonly List<AccountingEntry> _entries = [];

        public IReadOnlyList<AccountingEntry> Entries => _entries;

        public Task<ChartOfAccounts> GetChartOfAccountsAsync(CancellationToken ct = default)
            => Task.FromResult(new ChartOfAccounts());

        public void Post(
            Guid documentId,
            DateTime period,
            Account debit,
            Account credit,
            decimal amount,
            DimensionBag? debitDimensions = null,
            DimensionBag? creditDimensions = null,
            bool isStorno = false)
        {
            _entries.Add(new AccountingEntry
            {
                DocumentId = documentId,
                Period = period,
                Debit = debit,
                Credit = credit,
                Amount = amount,
                DebitDimensions = debitDimensions ?? DimensionBag.Empty,
                CreditDimensions = creditDimensions ?? DimensionBag.Empty,
                IsStorno = isStorno
            });
        }

        public void Clear() => _entries.Clear();
    }
}
