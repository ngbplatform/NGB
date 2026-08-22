using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Reports.AccountingConsistency;
using NGB.Accounting.Turnovers;
using NGB.Persistence.Locks;
using NGB.Persistence.Periods;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.Runtime.Accounting;
using NGB.Runtime.Maintenance;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Maintenance;

public sealed class AccountingRebuildServiceFullCoverageTests
{
    private static readonly DateOnly March = new(2026, 3, 1);

    [Fact]
    public async Task VerifyAsync_NormalizesOptionalPreviousPeriod_AndForwardsCancellation()
    {
        var fixture = new Fixture();

        var withPrevious = await fixture.Sut.VerifyAsync(
            March,
            new DateOnly(2026, 2, 28),
            fixture.Token);
        var withoutPrevious = await fixture.Sut.VerifyAsync(March, ct: fixture.Token);

        withPrevious.Should().BeSameAs(fixture.Report);
        withoutPrevious.Should().BeSameAs(fixture.Report);
        fixture.ConsistencyReport.Verify(
            x => x.RunForPeriodAsync(March, new DateOnly(2026, 2, 1), fixture.Token),
            Times.Once);
        fixture.ConsistencyReport.Verify(
            x => x.RunForPeriodAsync(March, null, fixture.Token),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_WhenPeriodIsNotMonthStart_FailsBeforeReading()
    {
        var fixture = new Fixture();

        var act = () => fixture.Sut.VerifyAsync(new DateOnly(2026, 3, 2), ct: fixture.Token);

        (await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>())
            .Which.ParamName.Should().Be("period");
        fixture.ConsistencyReport.Verify(
            x => x.RunForPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RebuildTurnoversAsync_NormalizesPeriod_WritesComputedRows_AndCommits()
    {
        var fixture = new Fixture();
        AccountingTurnover[] computed =
        [
            new() { Period = March, AccountId = Guid.CreateVersion7(), DebitAmount = 10m },
            new() { Period = March, AccountId = Guid.CreateVersion7(), CreditAmount = 4m }
        ];
        fixture.TurnoverAggregation
            .Setup(x => x.GetAggregatedFromRegisterAsync(March, fixture.Token))
            .ReturnsAsync(computed);

        var result = await fixture.Sut.RebuildTurnoversAsync(new DateOnly(2026, 3, 31), fixture.Token);

        result.Should().Be(2);
        fixture.TurnoverWriter.Verify(x => x.DeleteForPeriodAsync(March, fixture.Token), Times.Once);
        fixture.TurnoverWriter.Verify(
            x => x.WriteAsync(It.Is<IEnumerable<AccountingTurnover>>(rows => ReferenceEquals(rows, computed)), fixture.Token),
            Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(fixture.Token), Times.Once);
    }

    [Fact]
    public async Task RebuildTurnoversAsync_WhenAggregationFails_RollsBack_Logs_AndRethrows()
    {
        var fixture = new Fixture();
        fixture.TurnoverAggregation
            .Setup(x => x.GetAggregatedFromRegisterAsync(March, fixture.Token))
            .ThrowsAsync(new InvalidOperationException("aggregation failed"));

        var act = () => fixture.Sut.RebuildTurnoversAsync(March, fixture.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("aggregation failed");
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Logger.Invocations.Count(IsLogLevel(LogLevel.Error)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RebuildBalancesAsync_WhenNegativeBalancePolicyWarn_SavesBalance_AndLogsWarning()
    {
        var fixture = new Fixture();
        var account = fixture.AddAccount(NegativeBalancePolicy.Warn);
        fixture.TurnoverReader
            .Setup(x => x.GetForPeriodAsync(March, fixture.Token))
            .ReturnsAsync(
            [
                new AccountingTurnover
                {
                    Period = March,
                    AccountId = account.Id,
                    CreditAmount = 42m
                }
            ]);

        var result = await fixture.Sut.RebuildBalancesAsync(new DateOnly(2026, 3, 31), fixture.Token);

        result.Should().Be(1);
        fixture.BalanceWriter.Verify(
            x => x.SaveAsync(
                It.Is<IEnumerable<AccountingBalance>>(rows =>
                    rows.Single().AccountId == account.Id && rows.Single().ClosingBalance == -42m),
                fixture.Token),
            Times.Once);
        fixture.Logger.Invocations.Count(IsLogLevel(LogLevel.Warning)).Should().Be(1);
        fixture.Uow.Verify(x => x.CommitAsync(fixture.Token), Times.Once);
    }

    [Fact]
    public async Task RebuildBalancesAsync_WhenNegativeBalancePolicyForbids_RejectsAndRollsBackBeforeWriting()
    {
        var fixture = new Fixture();
        var account = fixture.AddAccount(NegativeBalancePolicy.Forbid);
        fixture.TurnoverReader
            .Setup(x => x.GetForPeriodAsync(March, fixture.Token))
            .ReturnsAsync(
            [
                new AccountingTurnover
                {
                    Period = March,
                    AccountId = account.Id,
                    AccountCode = account.Code,
                    CreditAmount = 7m
                }
            ]);

        var act = () => fixture.Sut.RebuildBalancesAsync(March, fixture.Token);

        var exception = await act.Should().ThrowAsync<AccountingNegativeBalanceForbiddenException>();
        exception.Which.Message.Should().ContainAll(account.Code, account.Name, "-7", "2026-03-01");
        fixture.BalanceWriter.Verify(
            x => x.DeleteForPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.BalanceWriter.Verify(
            x => x.SaveAsync(It.IsAny<IEnumerable<AccountingBalance>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
        fixture.Logger.Invocations.Count(IsLogLevel(LogLevel.Error)).Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RebuildAndVerifyAsync_CompletesWholeWorkflow_ForBothChainCheckModes(bool includePrevious)
    {
        var fixture = new Fixture();
        var inputPrevious = includePrevious ? new DateOnly(2026, 2, 28) : (DateOnly?)null;
        var expectedPrevious = includePrevious ? new DateOnly(2026, 2, 1) : (DateOnly?)null;

        var result = await fixture.Sut.RebuildAndVerifyAsync(
            new DateOnly(2026, 3, 31),
            inputPrevious,
            fixture.Token);

        result.Period.Should().Be(March);
        result.TurnoverRowsWritten.Should().Be(0);
        result.BalanceRowsWritten.Should().Be(0);
        result.VerifyReport.Should().BeSameAs(fixture.Report);
        fixture.ConsistencyReport.Verify(
            x => x.RunForPeriodAsync(March, expectedPrevious, fixture.Token),
            Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(fixture.Token), Times.Once);
        fixture.Logger.Invocations.Count(IsLogLevel(LogLevel.Information)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RebuildMonthAsync_WhenLockedWorkFails_RollsBack_Logs_AndRethrows()
    {
        var fixture = new Fixture();
        fixture.AdvisoryLocks
            .Setup(x => x.LockPeriodAsync(March, fixture.Token))
            .ThrowsAsync(new InvalidOperationException("lock failed"));

        var act = () => fixture.Sut.RebuildMonthAsync(March, ct: fixture.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("lock failed");
        fixture.Uow.Verify(x => x.RollbackAsync(fixture.Token), Times.Once);
        fixture.Logger.Invocations.Count(IsLogLevel(LogLevel.Error)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RebuildMonthAsync_WhenPeriodIsClosed_FailsBeforeStartingTransaction()
    {
        var fixture = new Fixture();
        fixture.ClosedPeriods
            .Setup(x => x.IsClosedAsync(March, fixture.Token))
            .ReturnsAsync(true);

        var act = () => fixture.Sut.RebuildMonthAsync(new DateOnly(2026, 3, 31), ct: fixture.Token);

        var exception = await act.Should().ThrowAsync<AccountingRebuildPeriodClosedException>();
        exception.Which.Period.Should().Be(March);
        fixture.Uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.AdvisoryLocks.Verify(
            x => x.LockPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Func<Moq.IInvocation, bool> IsLogLevel(LogLevel level)
        => invocation => invocation.Arguments.Count > 0 && Equals(invocation.Arguments[0], level);

    private sealed class Fixture
    {
        private readonly ChartOfAccounts _chartOfAccounts = new();

        public Fixture()
        {
            Uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
            Uow.Setup(x => x.BeginTransactionAsync(Token)).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(Token)).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(Token)).Returns(Task.CompletedTask);

            ClosedPeriods
                .Setup(x => x.IsClosedAsync(It.IsAny<DateOnly>(), Token))
                .ReturnsAsync(false);
            AdvisoryLocks
                .Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), Token))
                .Returns(Task.CompletedTask);
            TurnoverAggregation
                .Setup(x => x.GetAggregatedFromRegisterAsync(It.IsAny<DateOnly>(), Token))
                .ReturnsAsync(Array.Empty<AccountingTurnover>());
            TurnoverReader
                .Setup(x => x.GetForPeriodAsync(It.IsAny<DateOnly>(), Token))
                .ReturnsAsync(Array.Empty<AccountingTurnover>());
            BalanceReader
                .Setup(x => x.GetForPeriodAsync(It.IsAny<DateOnly>(), Token))
                .ReturnsAsync(Array.Empty<AccountingBalance>());
            TurnoverWriter
                .Setup(x => x.DeleteForPeriodAsync(It.IsAny<DateOnly>(), Token))
                .Returns(Task.CompletedTask);
            TurnoverWriter
                .Setup(x => x.WriteAsync(It.IsAny<IEnumerable<AccountingTurnover>>(), Token))
                .Returns(Task.CompletedTask);
            BalanceWriter
                .Setup(x => x.DeleteForPeriodAsync(It.IsAny<DateOnly>(), Token))
                .Returns(Task.CompletedTask);
            BalanceWriter
                .Setup(x => x.SaveAsync(It.IsAny<IEnumerable<AccountingBalance>>(), Token))
                .Returns(Task.CompletedTask);
            ConsistencyReport
                .Setup(x => x.RunForPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly?>(), Token))
                .ReturnsAsync(Report);
            ChartOfAccountsProvider
                .Setup(x => x.GetAsync(Token))
                .ReturnsAsync(_chartOfAccounts);

            var negativeBalanceChecker = new AccountingNegativeBalanceChecker(ChartOfAccountsProvider.Object);
            Sut = new AccountingRebuildService(
                Uow.Object,
                AdvisoryLocks.Object,
                ClosedPeriods.Object,
                TurnoverAggregation.Object,
                TurnoverReader.Object,
                BalanceReader.Object,
                TurnoverWriter.Object,
                BalanceWriter.Object,
                new AccountingBalanceCalculator(),
                negativeBalanceChecker,
                ConsistencyReport.Object,
                Logger.Object);
        }

        public CancellationToken Token { get; } = new CancellationTokenSource().Token;
        public AccountingConsistencyReport Report { get; } = new() { Period = March };
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IAdvisoryLockManager> AdvisoryLocks { get; } = new();
        public Mock<IClosedPeriodRepository> ClosedPeriods { get; } = new();
        public Mock<IAccountingTurnoverAggregationReader> TurnoverAggregation { get; } = new();
        public Mock<IAccountingTurnoverReader> TurnoverReader { get; } = new();
        public Mock<IAccountingBalanceReader> BalanceReader { get; } = new();
        public Mock<IAccountingTurnoverWriter> TurnoverWriter { get; } = new();
        public Mock<IAccountingBalanceWriter> BalanceWriter { get; } = new();
        public Mock<IAccountingConsistencyReportReader> ConsistencyReport { get; } = new();
        public Mock<IChartOfAccountsProvider> ChartOfAccountsProvider { get; } = new();
        public Mock<ILogger<AccountingRebuildService>> Logger { get; } = new();
        public AccountingRebuildService Sut { get; }

        public Account AddAccount(NegativeBalancePolicy policy)
        {
            var account = new Account(
                Guid.CreateVersion7(),
                "1010",
                "Cash",
                AccountType.Asset,
                negativeBalancePolicy: policy);
            _chartOfAccounts.Add(account);
            return account;
        }
    }
}
