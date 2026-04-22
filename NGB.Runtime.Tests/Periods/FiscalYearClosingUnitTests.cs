using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Reports.TrialBalance;
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
using NGB.Runtime.AuditLog;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Periods;

public sealed class FiscalYearClosingUnitTests
{
    [Fact]
    public async Task CloseFiscalYearAsync_NoProfitAndLossActivity_WritesPostingLogOnly()
    {
        // Arrange
        var fiscalYearEndPeriod = new DateOnly(2026, 12, 1);
        var retainedEarningsId = Guid.CreateVersion7();
        var assetId = Guid.CreateVersion7();

        var chart = new ChartOfAccounts();
        chart.Add(new Account(
            retainedEarningsId,
            "3000",
            "Retained Earnings",
            AccountType.Equity,
            StatementSection.Equity));

        // A non-P&L account that is present in the chart; trial balance may include it.
        chart.Add(new Account(
            assetId,
            "1000",
            "Cash",
            AccountType.Asset,
            StatementSection.Assets));

        // Add a non-P&L account row to TB (Assets) with zero closing so it doesn't trigger closing movements.
        var tbRows = new List<TrialBalanceRow>
        {
            new()
            {
                AccountId = assetId,
                AccountCode = "1000",
                ClosingBalance = 0m
            }
        };

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var turnoverReader = new Mock<IAccountingTurnoverReader>(MockBehavior.Strict);
        var balanceReader = new Mock<IAccountingBalanceReader>(MockBehavior.Strict);
        var balanceWriter = new Mock<IAccountingBalanceWriter>(MockBehavior.Strict);

        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(fiscalYearEndPeriod, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Prior months must be closed; set IsClosedAsync=true for all months before fiscalYearEndPeriod.
        closedPeriods.Setup(x => x.IsClosedAsync(It.Is<DateOnly>(d => d < fiscalYearEndPeriod), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var closedPeriodReader = new Mock<IClosedPeriodReader>(MockBehavior.Strict);
        closedPeriodReader.Setup(x => x.GetLatestClosedPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);

        var activityReader = new Mock<IAccountingPeriodActivityReader>(MockBehavior.Strict);
        activityReader.Setup(x => x.GetEarliestActivityPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);

        var chartProvider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        chartProvider.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(chart);

        var trialBalance = new Mock<ITrialBalanceReader>(MockBehavior.Strict);
        trialBalance.Setup(x => x.GetAsync(
                It.IsAny<DateOnly>(),
                fiscalYearEndPeriod,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tbRows);

        // Poison PostingEngine: if CloseFiscalYear tries to post entries (it shouldn't), test will fail immediately.
        var postingEngine = CreatePoisonPostingEngine();

        var calculator = new AccountingBalanceCalculator();

        var integrity = new Mock<IAccountingIntegrityChecker>(MockBehavior.Strict);

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        postingLog.Setup(x => x.TryBeginAsync(
                It.IsAny<Guid>(),
                PostingOperation.CloseFiscalYear,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.Begun);

        postingLog.Setup(x => x.MarkCompletedAsync(
                It.IsAny<Guid>(),
                PostingOperation.CloseFiscalYear,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var postingStateReader = new Mock<IPostingStateReader>(MockBehavior.Strict);
        postingStateReader.Setup(x => x.GetPageAsync(
                It.Is<PostingStatePageRequest>(r =>
                    r.Operation == PostingOperation.CloseFiscalYear &&
                    r.DocumentId != Guid.Empty &&
                    r.PageSize == 1),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostingStatePage([], HasMore: false, NextCursor: null));
        var auditEventReader = CreateEmptyAuditEventReader();

        var audit = new Mock<IAuditLogService>(MockBehavior.Loose);
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
        await svc.CloseFiscalYearAsync(fiscalYearEndPeriod, retainedEarningsId, closedBy: "tester", ct: CancellationToken.None);

        // Assert
        postingLog.Verify(x => x.TryBeginAsync(
                It.IsAny<Guid>(),
                PostingOperation.CloseFiscalYear,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()), Times.Once);

        postingLog.Verify(x => x.MarkCompletedAsync(
                It.IsAny<Guid>(),
                PostingOperation.CloseFiscalYear,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()), Times.Once);

        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CloseFiscalYearAsync_RetainedEarningsNotEquity_Throws()
    {
        // Arrange
        var fiscalYearEndPeriod = new DateOnly(2026, 12, 1);
        var retainedEarningsId = Guid.CreateVersion7();

        var chart = new ChartOfAccounts();
        chart.Add(new Account(
            retainedEarningsId,
            "9999",
            "Wrong Retained Earnings",
            AccountType.Asset,
            StatementSection.Assets));

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        var turnoverReader = new Mock<IAccountingTurnoverReader>(MockBehavior.Strict);
        var balanceReader = new Mock<IAccountingBalanceReader>(MockBehavior.Strict);
        var balanceWriter = new Mock<IAccountingBalanceWriter>(MockBehavior.Strict);

        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(fiscalYearEndPeriod, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        
        closedPeriods.Setup(x => x.IsClosedAsync(It.Is<DateOnly>(d => d < fiscalYearEndPeriod), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        var postingStateReader = new Mock<IPostingStateReader>(MockBehavior.Loose);
        var auditEventReader = CreateEmptyAuditEventReader();

        var audit = new Mock<IAuditLogService>(MockBehavior.Loose);
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
        Func<Task> act = async () => await svc.CloseFiscalYearAsync(fiscalYearEndPeriod, retainedEarningsId, closedBy: "tester", ct: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Retained earnings account must belong to Equity*");
    }

    [Fact]
    public async Task CloseFiscalYearAsync_WhenLaterMonthAlreadyClosed_ThrowsConflict()
    {
        var fiscalYearEndPeriod = new DateOnly(2026, 1, 1);
        var retainedEarningsId = Guid.CreateVersion7();
        var laterClosedPeriod = new DateOnly(2026, 3, 1);

        var chart = new ChartOfAccounts();
        chart.Add(new Account(
            retainedEarningsId,
            "3000",
            "Retained Earnings",
            AccountType.Equity,
            StatementSection.Equity));

        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(fiscalYearEndPeriod, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var closedPeriodReader = new Mock<IClosedPeriodReader>(MockBehavior.Strict);
        closedPeriodReader.Setup(x => x.GetLatestClosedPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(laterClosedPeriod);
        closedPeriodReader.Setup(x => x.GetClosedAsync(fiscalYearEndPeriod, laterClosedPeriod, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ClosedPeriodRecord { Period = laterClosedPeriod, ClosedBy = "tester", ClosedAtUtc = DateTime.UtcNow }
            ]);

        var activityReader = new Mock<IAccountingPeriodActivityReader>(MockBehavior.Strict);
        activityReader.Setup(x => x.GetEarliestActivityPeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(fiscalYearEndPeriod);

        var chartProvider = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        chartProvider.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chart);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockPeriodAsync(fiscalYearEndPeriod, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
            chartProvider.Object,
            new Mock<ITrialBalanceReader>(MockBehavior.Strict).Object,
            CreatePoisonPostingEngine(),
            new AccountingBalanceCalculator(),
            new Mock<IAccountingIntegrityChecker>(MockBehavior.Strict).Object,
            new Mock<IPostingStateRepository>(MockBehavior.Strict).Object,
            new Mock<IPostingStateReader>(MockBehavior.Strict).Object,
            CreateEmptyAuditEventReader().Object,
            new AccountingNegativeBalanceChecker(chartProvider.Object),
            new Mock<IAccountByIdResolver>(MockBehavior.Strict).Object,
            new Mock<ILogger<PeriodClosingService>>(MockBehavior.Loose).Object);

        var act = () => svc.CloseFiscalYearAsync(
            fiscalYearEndPeriod,
            retainedEarningsId,
            closedBy: "tester",
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<FiscalYearClosingBlockedByLaterClosedPeriodException>()
            .Where(x => x.LatestClosedPeriod == laterClosedPeriod);
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
