using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Periods;
using NGB.Accounting.Turnovers;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Checkers;
using NGB.Persistence.Locks;
using NGB.Persistence.Periods;
using NGB.Persistence.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.Runtime.Accounting;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Periods;

public sealed class PeriodClosingServiceTests
{
    [Fact]
    public async Task CloseMonthAsync_ClosedByRequired_Throws()
    {
        var svc = new PeriodClosingService(
            new Mock<IUnitOfWork>(MockBehavior.Loose).Object,
            new Mock<IAuditLogService>(MockBehavior.Loose).Object,
            new Mock<IAdvisoryLockManager>(MockBehavior.Loose).Object,
            new Mock<IAccountingTurnoverReader>(MockBehavior.Loose).Object,
            new Mock<IAccountingTurnoverAggregationReader>(MockBehavior.Loose).Object,
            new Mock<IAccountingTurnoverWriter>(MockBehavior.Loose).Object,
            new Mock<IAccountingBalanceReader>(MockBehavior.Loose).Object,
            new Mock<IAccountingBalanceWriter>(MockBehavior.Loose).Object,
            new Mock<IAccountingEntryMaintenanceWriter>(MockBehavior.Loose).Object,
            new Mock<IClosedPeriodRepository>(MockBehavior.Loose).Object,
            new Mock<IClosedPeriodReader>(MockBehavior.Loose).Object,
            new Mock<IAccountingPeriodActivityReader>(MockBehavior.Loose).Object,
            new Mock<IChartOfAccountsProvider>(MockBehavior.Loose).Object,
            new Mock<ITrialBalanceReader>(MockBehavior.Loose).Object,
            CreatePoisonPostingEngine(),
            new AccountingBalanceCalculator(),
            new Mock<IAccountingIntegrityChecker>(MockBehavior.Loose).Object,
            new Mock<IPostingStateRepository>(MockBehavior.Loose).Object,
            new Mock<IPostingStateReader>(MockBehavior.Loose).Object,
            CreateEmptyAuditEventReader().Object,
            new AccountingNegativeBalanceChecker(new Mock<IChartOfAccountsProvider>(MockBehavior.Loose).Object),
            new Mock<IAccountByIdResolver>(MockBehavior.Loose).Object,
            new Mock<ILogger<PeriodClosingService>>(MockBehavior.Loose).Object);

        var act = () => svc.CloseMonthAsync(new DateOnly(2026, 1, 1), closedBy: "  ", ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbArgumentRequiredException>()
            .Where(x => x.ParamName == "closedBy");
    }

    [Fact]
    public async Task CloseMonthAsync_PeriodAlreadyClosed_Throws_AndRollsBack()
    {
        // Arrange
        var period = new DateOnly(2026, 1, 1);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var turnoverReader = new Mock<IAccountingTurnoverReader>(MockBehavior.Strict);
        var balanceReader = new Mock<IAccountingBalanceReader>(MockBehavior.Strict);
        var balanceWriter = new Mock<IAccountingBalanceWriter>(MockBehavior.Strict);

        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(period, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var closedPeriodReader = new Mock<IClosedPeriodReader>(MockBehavior.Loose);
        var activityReader = new Mock<IAccountingPeriodActivityReader>(MockBehavior.Loose);

        var chartProvider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        chartProvider.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChartOfAccounts());

        var trialBalance = new Mock<ITrialBalanceReader>(MockBehavior.Strict);

        // A PostingEngine instance is required by constructor, but must not be used in CloseMonth scenario tests.
        var postingEngine = CreatePoisonPostingEngine();

        var calculator = new AccountingBalanceCalculator();
        var integrity = new Mock<IAccountingIntegrityChecker>(MockBehavior.Strict);
        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        var postingStateReader = new Mock<IPostingStateReader>(MockBehavior.Loose);
        var auditEventReader = CreateEmptyAuditEventReader();

        var audit = new Mock<IAuditLogService>(MockBehavior.Strict);
        var negativeChecker = new AccountingNegativeBalanceChecker(chartProvider.Object);
        var accountByIdResolver = new Mock<IAccountByIdResolver>(MockBehavior.Loose);
        var logger = new Mock<ILogger<PeriodClosingService>>();

        var svc = new PeriodClosingService(
            uow.Object,
            audit.Object,
            locks.Object,
            turnoverReader.Object,
            new Mock<IAccountingTurnoverAggregationReader>(MockBehavior.Loose).Object,
            new Mock<IAccountingTurnoverWriter>(MockBehavior.Loose).Object,
            balanceReader.Object,
            balanceWriter.Object,
            new Mock<IAccountingEntryMaintenanceWriter>(MockBehavior.Loose).Object,
            closedPeriods.Object,
            closedPeriodReader.Object,
            activityReader.Object,
            chartProvider.Object,
            trialBalance.Object,
            postingEngine,
            calculator,
            integrity.Object,
            postingLog.Object,
            postingStateReader.Object,
            auditEventReader.Object,
            negativeChecker,
            accountByIdResolver.Object,
            logger.Object);

        // Act
        Func<Task> act = async () => await svc.CloseMonthAsync(period, closedBy: "tester", ct: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<PeriodAlreadyClosedException>();

        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CloseMonthAsync_NegativeBalanceForbid_Throws_AndDoesNotMarkClosed()
    {
        // Arrange
        var period = new DateOnly(2026, 1, 1);
        var accountId = Guid.CreateVersion7();

        var chart = new ChartOfAccounts();
        chart.Add(new Account(
            accountId,
            "41",
            "Inventory",
            AccountType.Asset,
            StatementSection.Assets,
            negativeBalancePolicy: NegativeBalancePolicy.Forbid));

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var turnoverReader = new Mock<IAccountingTurnoverReader>(MockBehavior.Strict);
        turnoverReader.Setup(x => x.GetForPeriodAsync(period, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountingTurnover>
            {
                new()
                {
                    Period = period,
                    AccountId = accountId,
                    DebitAmount = 0m,
                    CreditAmount = 10m
                }
            });

        var balanceReader = new Mock<IAccountingBalanceReader>(MockBehavior.Strict);
        balanceReader.Setup(x => x.GetForPeriodAsync(period.AddMonths(-1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccountingBalance>());

        var balanceWriter = new Mock<IAccountingBalanceWriter>(MockBehavior.Strict);

        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(period, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var closedPeriodReader = new Mock<IClosedPeriodReader>(MockBehavior.Strict);
        closedPeriodReader.Setup(x => x.GetLatestClosedPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);

        var activityReader = new Mock<IAccountingPeriodActivityReader>(MockBehavior.Strict);
        activityReader.Setup(x => x.GetEarliestActivityPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);

        var chartProvider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        chartProvider.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(chart);

        var trialBalance = new Mock<ITrialBalanceReader>(MockBehavior.Strict);

        var postingEngine = CreatePoisonPostingEngine();

        var calculator = new AccountingBalanceCalculator();

        var integrity = new Mock<IAccountingIntegrityChecker>(MockBehavior.Strict);
        integrity.Setup(x => x.AssertPeriodIsBalancedAsync(period, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        var postingStateReader = new Mock<IPostingStateReader>(MockBehavior.Loose);
        var auditEventReader = CreateEmptyAuditEventReader();

        var audit = new Mock<IAuditLogService>(MockBehavior.Strict);
        var negativeChecker = new AccountingNegativeBalanceChecker(chartProvider.Object);
        var accountByIdResolver = new Mock<IAccountByIdResolver>(MockBehavior.Loose);
        var logger = new Mock<ILogger<PeriodClosingService>>();

        var svc = new PeriodClosingService(
            uow.Object,
            audit.Object,
            locks.Object,
            turnoverReader.Object,
            new Mock<IAccountingTurnoverAggregationReader>(MockBehavior.Loose).Object,
            new Mock<IAccountingTurnoverWriter>(MockBehavior.Loose).Object,
            balanceReader.Object,
            balanceWriter.Object,
            new Mock<IAccountingEntryMaintenanceWriter>(MockBehavior.Loose).Object,
            closedPeriods.Object,
            closedPeriodReader.Object,
            activityReader.Object,
            chartProvider.Object,
            trialBalance.Object,
            postingEngine,
            calculator,
            integrity.Object,
            postingLog.Object,
            postingStateReader.Object,
            auditEventReader.Object,
            negativeChecker,
            accountByIdResolver.Object,
            logger.Object);

        // Act
        Func<Task> act = async () => await svc.CloseMonthAsync(period, closedBy: "tester", ct: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>()
            .WithMessage("*Negative balance forbidden*");

        balanceWriter.Verify(x => x.SaveAsync(It.IsAny<IReadOnlyList<AccountingBalance>>(), It.IsAny<CancellationToken>()), Times.Never);
        closedPeriods.Verify(x => x.MarkClosedAsync(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CloseMonthAsync_WhenEarlierActivityMonthIsOpen_ThrowsPrerequisiteNotMet()
    {
        var period = new DateOnly(2026, 2, 1);
        var earliestActivity = new DateOnly(2026, 1, 1);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockPeriodAsync(period, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(period, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var closedPeriodReader = new Mock<IClosedPeriodReader>(MockBehavior.Strict);
        closedPeriodReader.Setup(x => x.GetLatestClosedPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);

        var activityReader = new Mock<IAccountingPeriodActivityReader>(MockBehavior.Strict);
        activityReader.Setup(x => x.GetEarliestActivityPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(earliestActivity);

        var svc = new PeriodClosingService(
            uow.Object,
            new Mock<IAuditLogService>(MockBehavior.Strict).Object,
            locks.Object,
            new Mock<IAccountingTurnoverReader>(MockBehavior.Strict).Object,
            new Mock<IAccountingTurnoverAggregationReader>(MockBehavior.Loose).Object,
            new Mock<IAccountingTurnoverWriter>(MockBehavior.Loose).Object,
            new Mock<IAccountingBalanceReader>(MockBehavior.Strict).Object,
            new Mock<IAccountingBalanceWriter>(MockBehavior.Strict).Object,
            new Mock<IAccountingEntryMaintenanceWriter>(MockBehavior.Loose).Object,
            closedPeriods.Object,
            closedPeriodReader.Object,
            activityReader.Object,
            new Mock<IChartOfAccountsProvider>(MockBehavior.Strict).Object,
            new Mock<ITrialBalanceReader>(MockBehavior.Strict).Object,
            CreatePoisonPostingEngine(),
            new AccountingBalanceCalculator(),
            new Mock<IAccountingIntegrityChecker>(MockBehavior.Strict).Object,
            new Mock<IPostingStateRepository>(MockBehavior.Strict).Object,
            new Mock<IPostingStateReader>(MockBehavior.Strict).Object,
            CreateEmptyAuditEventReader().Object,
            new AccountingNegativeBalanceChecker(new Mock<IChartOfAccountsProvider>(MockBehavior.Loose).Object),
            new Mock<IAccountByIdResolver>(MockBehavior.Strict).Object,
            new Mock<ILogger<PeriodClosingService>>(MockBehavior.Loose).Object);

        var act = () => svc.CloseMonthAsync(period, closedBy: "tester", ct: CancellationToken.None);

        await act.Should().ThrowAsync<MonthClosingPrerequisiteNotMetException>()
            .Where(x => x.NextClosablePeriod == earliestActivity);

        closedPeriods.Verify(x => x.MarkClosedAsync(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReopenMonthAsync_WhenRequestedPeriodIsNotLatestClosed_ThrowsConflict()
    {
        var requestedPeriod = new DateOnly(2026, 1, 1);
        var latestClosedPeriod = new DateOnly(2026, 2, 1);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockPeriodAsync(requestedPeriod, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(requestedPeriod, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var closedPeriodReader = new Mock<IClosedPeriodReader>(MockBehavior.Strict);
        closedPeriodReader.Setup(x => x.GetLatestClosedPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestClosedPeriod);
        closedPeriodReader.Setup(x => x.GetClosedAsync(requestedPeriod, latestClosedPeriod, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ClosedPeriodRecord { Period = requestedPeriod, ClosedBy = "tester", ClosedAtUtc = DateTime.UtcNow },
                new ClosedPeriodRecord { Period = latestClosedPeriod, ClosedBy = "tester", ClosedAtUtc = DateTime.UtcNow }
            ]);

        var activityReader = new Mock<IAccountingPeriodActivityReader>(MockBehavior.Strict);
        activityReader.Setup(x => x.GetEarliestActivityPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(requestedPeriod);

        var svc = new PeriodClosingService(
            uow.Object,
            new Mock<IAuditLogService>(MockBehavior.Strict).Object,
            locks.Object,
            new Mock<IAccountingTurnoverReader>(MockBehavior.Strict).Object,
            new Mock<IAccountingTurnoverAggregationReader>(MockBehavior.Loose).Object,
            new Mock<IAccountingTurnoverWriter>(MockBehavior.Loose).Object,
            new Mock<IAccountingBalanceReader>(MockBehavior.Strict).Object,
            new Mock<IAccountingBalanceWriter>(MockBehavior.Strict).Object,
            new Mock<IAccountingEntryMaintenanceWriter>(MockBehavior.Loose).Object,
            closedPeriods.Object,
            closedPeriodReader.Object,
            activityReader.Object,
            new Mock<IChartOfAccountsProvider>(MockBehavior.Strict).Object,
            new Mock<ITrialBalanceReader>(MockBehavior.Strict).Object,
            CreatePoisonPostingEngine(),
            new AccountingBalanceCalculator(),
            new Mock<IAccountingIntegrityChecker>(MockBehavior.Strict).Object,
            new Mock<IPostingStateRepository>(MockBehavior.Strict).Object,
            new Mock<IPostingStateReader>(MockBehavior.Strict).Object,
            CreateEmptyAuditEventReader().Object,
            new AccountingNegativeBalanceChecker(new Mock<IChartOfAccountsProvider>(MockBehavior.Loose).Object),
            new Mock<IAccountByIdResolver>(MockBehavior.Strict).Object,
            new Mock<ILogger<PeriodClosingService>>(MockBehavior.Loose).Object);

        var act = () => svc.ReopenMonthAsync(
            requestedPeriod,
            reopenedBy: "tester",
            reason: "Need to reopen the chain",
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<MonthReopenLatestClosedRequiredException>()
            .Where(x => x.LatestClosedPeriod == latestClosedPeriod);

        closedPeriods.Verify(x => x.ReopenAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PostingEngine CreatePoisonPostingEngine()
    {
        var contextFactory = new Mock<IAccountingPostingContextFactory>(MockBehavior.Strict);
        contextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("PostingEngine should not be used in this test."));

        var engineUow = new Mock<IUnitOfWork>(MockBehavior.Loose);
        engineUow.SetupGet(x => x.HasActiveTransaction).Returns(true);

        var engineLocks = new Mock<IAdvisoryLockManager>(MockBehavior.Loose);
        var engineEntryWriter = new Mock<IAccountingEntryWriter>(MockBehavior.Loose);
        var engineTurnoverWriter = new Mock<IAccountingTurnoverWriter>(MockBehavior.Loose);
        var engineDimensionSetService = new Mock<NGB.Runtime.Dimensions.IDimensionSetService>(MockBehavior.Loose);
        engineDimensionSetService
            .Setup(x => x.GetOrCreateIdAsync(It.IsAny<NGB.Core.Dimensions.DimensionBag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Empty);
        var engineOpBal = new Mock<IAccountingOperationalBalanceReader>(MockBehavior.Loose);
        var engineClosed = new Mock<IClosedPeriodRepository>(MockBehavior.Loose);
        var engineValidator = new Mock<NGB.Accounting.Posting.Validators.IAccountingPostingValidator>(MockBehavior.Loose);
        var enginePostingLog = new Mock<IPostingStateRepository>(MockBehavior.Loose);
        var engineLogger = new Mock<ILogger<PostingEngine>>(MockBehavior.Loose);

        return new PostingEngine(
            contextFactory.Object,
            engineUow.Object,
            engineLocks.Object,
            engineEntryWriter.Object,
            engineTurnoverWriter.Object,
            engineDimensionSetService.Object,
            engineOpBal.Object,
            engineClosed.Object,
            engineValidator.Object,
            enginePostingLog.Object,
            engineLogger.Object);
    }

    private static Mock<IAuditEventReader> CreateEmptyAuditEventReader()
    {
        var reader = new Mock<IAuditEventReader>(MockBehavior.Loose);
        reader.Setup(x => x.QueryAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditEvent>());
        return reader;
    }
}
