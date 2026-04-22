using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting;
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
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Posting;

public sealed class PostingEngineOrchestrationTests
{
    [Fact]
    public async Task PostAsync_WhenEntryProvidesDimensionSetIds_DoesNotOverwrite_AndDoesNotCallDimensionSetService()
    {
        // Arrange
        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var providedDebitSetId = Guid.CreateVersion7();
        var providedCreditSetId = Guid.CreateVersion7();

        var chart = CreateChart();
        var context = new TestPostingContext(chart);

        var contextFactory = new Mock<IAccountingPostingContextFactory>(MockBehavior.Strict);
        contextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(context);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        locks.Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        IReadOnlyList<AccountingEntry>? capturedEntries = null;
        var entryWriter = new Mock<IAccountingEntryWriter>(MockBehavior.Strict);
        entryWriter
            .Setup(x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<AccountingEntry>, CancellationToken>((e, _) => capturedEntries = e)
            .Returns(Task.CompletedTask);

        List<AccountingTurnover>? capturedTurnovers = null;
        var turnoverWriter = new Mock<IAccountingTurnoverWriter>(MockBehavior.Strict);
        turnoverWriter
            .Setup(x => x.WriteAsync(It.IsAny<IEnumerable<AccountingTurnover>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AccountingTurnover>, CancellationToken>((t, _) => capturedTurnovers = t.ToList())
            .Returns(Task.CompletedTask);

        var opBalReader = new Mock<IAccountingOperationalBalanceReader>(MockBehavior.Strict);

        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var validator = new Mock<NGB.Accounting.Posting.Validators.IAccountingPostingValidator>(MockBehavior.Strict);
        validator.Setup(x => x.Validate(It.IsAny<IReadOnlyList<AccountingEntry>>()));

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        postingLog.Setup(x => x.TryBeginAsync(
                documentId,
                PostingOperation.Post,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.Begun);
        postingLog.Setup(x => x.MarkCompletedAsync(
                documentId,
                PostingOperation.Post,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Strict mock: any call would fail the test.
        var dimensionSetService = new Mock<NGB.Runtime.Dimensions.IDimensionSetService>(MockBehavior.Strict);

        var logger = new Mock<ILogger<PostingEngine>>();

        var engine = new PostingEngine(
            contextFactory.Object,
            uow.Object,
            locks.Object,
            entryWriter.Object,
            turnoverWriter.Object,
            dimensionSetService.Object,
            opBalReader.Object,
            closedPeriods.Object,
            validator.Object,
            postingLog.Object,
            logger.Object);

        // Act
        var result = await engine.PostAsync(
            PostingOperation.Post,
            async (ctx, _) =>
            {
                chart.TryGetByCode("60", out var debit);
                chart.TryGetByCode("50", out var credit);
                ctx.Post(documentId, periodUtc, debit!, credit!, 100m);

                // Simulates a posting handler that already has resolved DimensionSetIds (e.g., from persisted draft lines).
                var entry = ((TestPostingContext)ctx).Entries[^1];
                entry.DebitDimensionSetId = providedDebitSetId;
                entry.CreditDimensionSetId = providedCreditSetId;
                await Task.CompletedTask;
            },
            manageTransaction: true,
            ct: CancellationToken.None);

        // Assert
        result.Should().Be(PostingResult.Executed);

        capturedEntries.Should().NotBeNull();
        capturedEntries!.Should().HaveCount(1);
        capturedEntries[0].DebitDimensionSetId.Should().Be(providedDebitSetId);
        capturedEntries[0].CreditDimensionSetId.Should().Be(providedCreditSetId);

        capturedTurnovers.Should().NotBeNull();
        capturedTurnovers!.Select(x => x.DimensionSetId).Should().BeEquivalentTo([providedDebitSetId, providedCreditSetId]);

        dimensionSetService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PostAsync_AlreadyCompleted_ReturnsAlreadyCompleted_AndDoesNotWrite()
    {
        // Arrange
        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var chart = CreateChart();
        var context = new TestPostingContext(chart);

        var contextFactory = new Mock<IAccountingPostingContextFactory>(MockBehavior.Strict);
        contextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(context);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // NOTE: in AlreadyCompleted branch we still take period locks and guards before returning? (Engine does TryBegin before writing,
        // but after entries are built and validated). We keep the locks setup permissive to match current behavior.
        locks.Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var entryWriter = new Mock<IAccountingEntryWriter>(MockBehavior.Strict);
        var turnoverWriter = new Mock<IAccountingTurnoverWriter>(MockBehavior.Strict);

        var opBalReader = new Mock<IAccountingOperationalBalanceReader>(MockBehavior.Strict);
        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var validator = new Mock<NGB.Accounting.Posting.Validators.IAccountingPostingValidator>(MockBehavior.Strict);
        validator.Setup(x => x.Validate(It.IsAny<IReadOnlyList<AccountingEntry>>()));

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        postingLog.Setup(x => x.TryBeginAsync(
                documentId,
                PostingOperation.Post,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.AlreadyCompleted);

        var dimensionSetService = new Mock<NGB.Runtime.Dimensions.IDimensionSetService>(MockBehavior.Loose);
        dimensionSetService
            .Setup(x => x.GetOrCreateIdAsync(It.IsAny<NGB.Core.Dimensions.DimensionBag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Empty);

        var logger = new Mock<ILogger<PostingEngine>>();

        var engine = new PostingEngine(
            contextFactory.Object,
            uow.Object,
            locks.Object,
            entryWriter.Object,
            turnoverWriter.Object,
            dimensionSetService.Object,
            opBalReader.Object,
            closedPeriods.Object,
            validator.Object,
            postingLog.Object,
            logger.Object);

        // Act
        var result = await engine.PostAsync(
            PostingOperation.Post,
            async (ctx, _) =>
            {
                chart.TryGetByCode("60", out var debit);
                chart.TryGetByCode("50", out var credit);
                ctx.Post(documentId, periodUtc, debit!, credit!, 100m);
                await Task.CompletedTask;
            },
            manageTransaction: true,
            ct: CancellationToken.None);

        // Assert
        result.Should().Be(PostingResult.AlreadyCompleted);

        entryWriter.Verify(x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingEntry>>(), It.IsAny<CancellationToken>()), Times.Never);
        turnoverWriter.Verify(x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingTurnover>>(), It.IsAny<CancellationToken>()), Times.Never);
        postingLog.Verify(x => x.MarkCompletedAsync(It.IsAny<Guid>(), It.IsAny<PostingOperation>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PostAsync_InProgress_Throws_AndRollsBack()
    {
        // Arrange
        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var chart = CreateChart();
        var context = new TestPostingContext(chart);

        var contextFactory = new Mock<IAccountingPostingContextFactory>(MockBehavior.Strict);
        contextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(context);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        locks.Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var entryWriter = new Mock<IAccountingEntryWriter>(MockBehavior.Strict);
        var turnoverWriter = new Mock<IAccountingTurnoverWriter>(MockBehavior.Strict);
        var opBalReader = new Mock<IAccountingOperationalBalanceReader>(MockBehavior.Strict);

        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var validator = new Mock<NGB.Accounting.Posting.Validators.IAccountingPostingValidator>(MockBehavior.Strict);
        validator.Setup(x => x.Validate(It.IsAny<IReadOnlyList<AccountingEntry>>()));

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        postingLog.Setup(x => x.TryBeginAsync(
                documentId,
                PostingOperation.Post,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.InProgress);

        var dimensionSetService = new Mock<NGB.Runtime.Dimensions.IDimensionSetService>(MockBehavior.Loose);
        dimensionSetService
            .Setup(x => x.GetOrCreateIdAsync(It.IsAny<NGB.Core.Dimensions.DimensionBag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Empty);

        var logger = new Mock<ILogger<PostingEngine>>();

        var engine = new PostingEngine(
            contextFactory.Object,
            uow.Object,
            locks.Object,
            entryWriter.Object,
            turnoverWriter.Object,
            dimensionSetService.Object,
            opBalReader.Object,
            closedPeriods.Object,
            validator.Object,
            postingLog.Object,
            logger.Object);

        // Act
        Func<Task> act = async () => await engine.PostAsync(
            PostingOperation.Post,
            async (ctx, _) =>
            {
                chart.TryGetByCode("60", out var debit);
                chart.TryGetByCode("50", out var credit);
                ctx.Post(documentId, periodUtc, debit!, credit!, 100m);
                await Task.CompletedTask;
            },
            manageTransaction: true,
            ct: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<PostingAlreadyInProgressException>()
            .WithMessage("*already in progress*");

        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);

        entryWriter.VerifyNoOtherCalls();
        turnoverWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PostAsync_ValidatorThrows_RollsBack_AndDoesNotWriteOrCompleteLog()
    {
        // Arrange
        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var chart = CreateChart();
        var context = new TestPostingContext(chart);

        var contextFactory = new Mock<IAccountingPostingContextFactory>(MockBehavior.Strict);
        contextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(context);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        locks.Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var entryWriter = new Mock<IAccountingEntryWriter>(MockBehavior.Strict);
        var turnoverWriter = new Mock<IAccountingTurnoverWriter>(MockBehavior.Strict);
        var opBalReader = new Mock<IAccountingOperationalBalanceReader>(MockBehavior.Strict);

        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var validator = new Mock<NGB.Accounting.Posting.Validators.IAccountingPostingValidator>(MockBehavior.Strict);
        validator.Setup(x => x.Validate(It.IsAny<IReadOnlyList<AccountingEntry>>()))
            .Throws(new NgbInvariantViolationException("validator failed"));

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        postingLog.Setup(x => x.TryBeginAsync(
                documentId,
                PostingOperation.Post,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.Begun);

        var dimensionSetService = new Mock<NGB.Runtime.Dimensions.IDimensionSetService>(MockBehavior.Loose);
        dimensionSetService
            .Setup(x => x.GetOrCreateIdAsync(It.IsAny<NGB.Core.Dimensions.DimensionBag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Empty);

        var logger = new Mock<ILogger<PostingEngine>>();

        var engine = new PostingEngine(
            contextFactory.Object,
            uow.Object,
            locks.Object,
            entryWriter.Object,
            turnoverWriter.Object,
            dimensionSetService.Object,
            opBalReader.Object,
            closedPeriods.Object,
            validator.Object,
            postingLog.Object,
            logger.Object);

        // Act
        Func<Task> act = async () => await engine.PostAsync(
            PostingOperation.Post,
            async (ctx, _) =>
            {
                chart.TryGetByCode("60", out var debit);
                chart.TryGetByCode("50", out var credit);
                ctx.Post(documentId, periodUtc, debit!, credit!, 100m);
                await Task.CompletedTask;
            },
            manageTransaction: true,
            ct: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*validator failed*");

        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        entryWriter.Verify(x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingEntry>>(), It.IsAny<CancellationToken>()), Times.Never);
        turnoverWriter.Verify(x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingTurnover>>(), It.IsAny<CancellationToken>()), Times.Never);
        postingLog.Verify(x => x.MarkCompletedAsync(It.IsAny<Guid>(), It.IsAny<PostingOperation>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ChartOfAccounts CreateChart()
    {
        var coa = new ChartOfAccounts();

        // Liability/Equity are Allow by default (so Operational NegativeBalance reader is not required for these tests)
        var acc60 = new Account(null, "60", "Accounts Payable", AccountType.Liability, StatementSection.Liabilities);
        var acc50 = new Account(null, "50", "Cash", AccountType.Equity, StatementSection.Equity);

        coa.Add(acc60);
        coa.Add(acc50);
        return coa;
    }

    private sealed class TestPostingContext(ChartOfAccounts chart) : IAccountingPostingContext
    {
        private readonly List<AccountingEntry> _entries = new();

        public IReadOnlyList<AccountingEntry> Entries => _entries;

        public Task<ChartOfAccounts> GetChartOfAccountsAsync(CancellationToken ct = default) => Task.FromResult(chart);

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
    }
}
