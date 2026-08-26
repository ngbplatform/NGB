using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Dimensions;
using NGB.Accounting.Periods;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Registers;
using NGB.Accounting.Reports.TrialBalance;
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
using NGB.Runtime.AuditLog;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.Tests.Periods;

public sealed class PeriodClosingServiceFullCoverageTests
{
    private static readonly DateOnly January = new(2026, 1, 1);
    private static readonly CancellationToken Ct = new CancellationTokenSource().Token;

    [Fact]
    public async Task CloseMonthSuccessNormalizesPersistsAuditsAndLogsWarnBalance()
    {
        var account = new Account(Guid.NewGuid(), "100", "Inventory", AccountType.Asset,
            negativeBalancePolicy: NegativeBalancePolicy.Warn);
        var f = new Fixture();
        f.Chart.Add(account);
        f.Turnovers =
        [
            new AccountingTurnover { Period = January, AccountId = account.Id, CreditAmount = 10m }
        ];

        await f.Sut.CloseMonthAsync(January.AddDays(20), "closer", Ct);

        f.BalanceWriter.Verify(x => x.SaveAsync(
            It.Is<IEnumerable<AccountingBalance>>(rows => rows.Single().ClosingBalance == -10m), Ct), Times.Once);
        f.ClosedPeriods.Verify(x => x.MarkClosedAsync(January, "closer", Fixture.Now, Ct), Times.Once);
        f.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Period,
            DeterministicGuid.Create("CloseMonth|2026-01-01"),
            AuditActionCodes.PeriodCloseMonth,
            It.Is<IReadOnlyList<AuditFieldChange>>(changes => changes.Count == 3),
            It.IsAny<object>(),
            It.IsAny<Guid?>(),
            Ct), Times.Once);
        f.Uow.Verify(x => x.CommitAsync(Ct), Times.Once);
    }

    [Fact]
    public async Task CloseMonthRejectsBrokenChainAndBrokenChainAfterLatestFallsThroughToPrerequisite()
    {
        var f = new Fixture
        {
            EarliestActivity = January,
            LatestClosed = January.AddMonths(2),
            ClosedRows = [Closed(January), Closed(January.AddMonths(2))]
        };

        await ((Func<Task>)(() => f.Sut.CloseMonthAsync(January.AddMonths(1), "closer", Ct)))
            .Should().ThrowAsync<MonthClosingBlockedByLaterClosedPeriodException>();

        await ((Func<Task>)(() => f.Sut.CloseMonthAsync(January.AddMonths(3), "closer", Ct)))
            .Should().ThrowAsync<MonthClosingPrerequisiteNotMetException>();
    }

    [Fact]
    public async Task CloseMonthAllowsFirstActivityAfterLatestClosedPeriod()
    {
        var march = January.AddMonths(2);
        var f = new Fixture
        {
            EarliestActivity = march,
            LatestClosed = January,
            ClosedRows = [Closed(January)]
        };

        await f.Sut.CloseMonthAsync(march, "closer", Ct);

        f.ClosedPeriods.Verify(x => x.MarkClosedAsync(march, "closer", Fixture.Now, Ct), Times.Once);
    }

    [Fact]
    public async Task ReopenMonthValidatesArgumentsAndAllLifecycleGuards()
    {
        var f = new Fixture();

        await ((Func<Task>)(() => f.Sut.ReopenMonthAsync(January, " ", "reason", Ct)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.ReopenMonthAsync(January, "actor", " ", Ct)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        f.IsClosed = false;
        await ((Func<Task>)(() => f.Sut.ReopenMonthAsync(January, "actor", "reason", Ct)))
            .Should().ThrowAsync<PeriodNotClosedException>();

        f.IsClosed = true;
        f.EarliestActivity = null;
        f.LatestClosed = null;
        await ((Func<Task>)(() => f.Sut.ReopenMonthAsync(January, "actor", "reason", Ct)))
            .Should().ThrowAsync<PeriodNotClosedException>();

        f.EarliestActivity = January;
        f.LatestClosed = January.AddMonths(1);
        f.ClosedRows = [Closed(January), Closed(January.AddMonths(1))];
        await ((Func<Task>)(() => f.Sut.ReopenMonthAsync(January, "actor", "reason", Ct)))
            .Should().ThrowAsync<MonthReopenLatestClosedRequiredException>();

        f.LatestClosed = January;
        f.ClosedRows = [Closed(January)];
        f.PostingRows = [Posting(January, PostingStateStatus.Completed)];
        await ((Func<Task>)(() => f.Sut.ReopenMonthAsync(January, "actor", "reason", Ct)))
            .Should().ThrowAsync<MonthReopenBlockedByFiscalYearCloseException>();
    }

    [Fact]
    public async Task ReopenMonthSuccessReopensAndAuditsTrimmedReason()
    {
        var f = new Fixture
        {
            IsClosed = true,
            EarliestActivity = January,
            LatestClosed = January,
            ClosedRows = [Closed(January)]
        };

        await f.Sut.ReopenMonthAsync(January.AddDays(14), "actor", "  correction  ", Ct);

        f.ClosedPeriods.Verify(x => x.ReopenAsync(January, Ct), Times.Once);
        f.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Period,
            DeterministicGuid.Create("CloseMonth|2026-01-01"),
            AuditActionCodes.PeriodReopenMonth,
            It.Is<IReadOnlyList<AuditFieldChange>>(changes => changes.Any(c =>
                c.FieldPath == "reopen_reason" && c.NewValueJson!.Contains("correction"))),
            It.IsAny<object>(), It.IsAny<Guid?>(), Ct), Times.Once);
    }

    [Fact]
    public async Task ReopenFiscalYearValidatesArgumentsDateAndPostingStates()
    {
        var f = new Fixture();

        await ((Func<Task>)(() => f.Sut.ReopenFiscalYearAsync(January, " ", "reason", Ct)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.ReopenFiscalYearAsync(January, "actor", " ", Ct)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.ReopenFiscalYearAsync(January.AddDays(1), "actor", "reason", Ct)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        await ((Func<Task>)(() => f.Sut.ReopenFiscalYearAsync(January, "actor", "reason", Ct)))
            .Should().ThrowAsync<FiscalYearNotClosedException>();

        f.PostingRows = [Posting(January, PostingStateStatus.InProgress)];
        await ((Func<Task>)(() => f.Sut.ReopenFiscalYearAsync(January, "actor", "reason", Ct)))
            .Should().ThrowAsync<FiscalYearReopenBlockedByInProgressException>();

        f.PostingRows = [Posting(January, PostingStateStatus.StaleInProgress)];
        await ((Func<Task>)(() => f.Sut.ReopenFiscalYearAsync(January, "actor", "reason", Ct)))
            .Should().ThrowAsync<FiscalYearReopenBlockedByInProgressException>();

        f.PostingRows = [Posting(January, (PostingStateStatus)99)];
        await ((Func<Task>)(() => f.Sut.ReopenFiscalYearAsync(January, "actor", "reason", Ct)))
            .Should().ThrowAsync<FiscalYearNotClosedException>();
    }

    [Fact]
    public async Task ReopenFiscalYearRejectsLaterClosedPeriodAndUnexpectedEntryPeriods()
    {
        var f = new Fixture
        {
            PostingRows = [Posting(January, PostingStateStatus.Completed)],
            LatestClosed = January.AddMonths(1),
            EarliestActivity = January,
            ClosedRows = [Closed(January), Closed(January.AddMonths(1))]
        };

        await ((Func<Task>)(() => f.Sut.ReopenFiscalYearAsync(January, "actor", "reason", Ct)))
            .Should().ThrowAsync<FiscalYearReopenBlockedByLaterClosedPeriodException>();

        f.LatestClosed = January;
        f.ClosedRows = [Closed(January)];
        f.DeletedEntryPeriods = [January.AddMonths(-1)];
        await ((Func<Task>)(() => f.Sut.ReopenFiscalYearAsync(January, "actor", "reason", Ct)))
            .Should().ThrowAsync<NgbInvariantViolationException>();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReopenFiscalYearSuccessRebuildsStateAndConditionallyReopensEndMonth(
        bool endPeriodClosed,
        bool hadClosingEntries)
    {
        var f = new Fixture
        {
            PostingRows = [Posting(January, PostingStateStatus.Completed)],
            IsClosed = endPeriodClosed,
            EarliestActivity = January,
            LatestClosed = endPeriodClosed ? January : null,
            ClosedRows = endPeriodClosed ? [Closed(January)] : [],
            DeletedEntryPeriods = hadClosingEntries ? [January] : [],
            AggregatedTurnovers =
            [
                new AccountingTurnover { Period = January, AccountId = Guid.NewGuid(), DebitAmount = 3m }
            ]
        };

        await f.Sut.ReopenFiscalYearAsync(January, "actor", "  correction  ", Ct);

        f.EntryMaintenance.Verify(x => x.DeleteByDocumentAsync(
            DeterministicGuid.Create("CloseFiscalYear|2026-01-01"), Ct), Times.Once);
        f.TurnoverWriter.Verify(x => x.DeleteForPeriodAsync(January, Ct), Times.Once);
        f.TurnoverWriter.Verify(x => x.WriteAsync(f.AggregatedTurnovers, Ct), Times.Once);
        f.BalanceWriter.Verify(x => x.DeleteForPeriodAsync(January, Ct), Times.Once);
        f.PostingState.Verify(x => x.ClearCompletedStateAsync(
            It.IsAny<Guid>(), PostingOperation.CloseFiscalYear, Ct), Times.Once);
        f.ClosedPeriods.Verify(x => x.ReopenAsync(January, Ct),
            endPeriodClosed ? Times.Once() : Times.Never());
        f.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Period, It.IsAny<Guid>(), AuditActionCodes.PeriodReopenFiscalYear,
            It.IsAny<IReadOnlyList<AuditFieldChange>>(), It.IsAny<object>(), It.IsAny<Guid?>(), Ct), Times.Once);
        f.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Period, It.IsAny<Guid>(), AuditActionCodes.PeriodReopenMonth,
            It.IsAny<IReadOnlyList<AuditFieldChange>>(), It.IsAny<object>(), It.IsAny<Guid?>(), Ct),
            endPeriodClosed ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task CloseFiscalYearValidatesIdentityDateMissingAccountNormalBalanceAndDimensions()
    {
        var f = new Fixture();

        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(January, Guid.Empty, "actor", Ct)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(January, Guid.NewGuid(), " ", Ct)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(January.AddDays(1), Guid.NewGuid(), "actor", Ct)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(January, Guid.NewGuid(), "actor", Ct)))
            .Should().ThrowAsync<NgbArgumentInvalidException>().WithMessage("*not found*");

        var debitNormalEquity = new Account(Guid.NewGuid(), "301", "Contra equity", AccountType.Equity,
            StatementSection.Equity, isContra: true);
        f.Chart.Add(debitNormalEquity);
        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(January, debitNormalEquity.Id, "actor", Ct)))
            .Should().ThrowAsync<NgbArgumentInvalidException>().WithMessage("*Credit-normal*");

        var dimensionsRequired = new Account(
            Guid.NewGuid(), "302", "Dimensioned equity", AccountType.Equity, StatementSection.Equity,
            dimensionRules: [new AccountDimensionRule(Guid.NewGuid(), "project", 1, true)]);
        f.Chart.Add(dimensionsRequired);
        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(January, dimensionsRequired.Id, "actor", Ct)))
            .Should().ThrowAsync<FiscalYearRetainedEarningsValidationException>();
    }

    [Fact]
    public async Task CloseFiscalYearPrerequisitesRejectClosedEndBrokenLaterAndEarlierOpenMonths()
    {
        var retained = Equity("300");
        var f = new Fixture { IsClosed = true };
        f.Chart.Add(retained);

        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(January, retained.Id, "actor", Ct)))
            .Should().ThrowAsync<PeriodAlreadyClosedException>();

        var march = January.AddMonths(2);
        var april = January.AddMonths(3);
        f.IsClosed = false;
        f.IsClosedQuery = p => p < march;
        f.EarliestActivity = January;
        f.LatestClosed = april;
        f.ClosedRows = [Closed(January), Closed(april)];
        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(march, retained.Id, "actor", Ct)))
            .Should().ThrowAsync<FiscalYearClosingBlockedByLaterClosedPeriodException>();

        f.ClosedRows = [Closed(January), Closed(January.AddMonths(1)), Closed(march), Closed(april)];
        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(march, retained.Id, "actor", Ct)))
            .Should().ThrowAsync<FiscalYearClosingBlockedByLaterClosedPeriodException>();

        f.EarliestActivity = null;
        f.LatestClosed = null;
        f.ClosedRows = [];
        f.IsClosedQuery = p => p == January;
        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(march, retained.Id, "actor", Ct)))
            .Should().ThrowAsync<FiscalYearClosingPrerequisiteNotMetException>()
            .Where(x => x.NotClosedPeriod == January.AddMonths(1));
    }

    [Fact]
    public async Task CloseFiscalYearCurrentStateRejectsInProgressAndCompletedWithSameOrUnknownAccount()
    {
        var retained = Equity("300");

        var inProgress = new Fixture { PostingRows = [Posting(January, PostingStateStatus.InProgress)] };
        inProgress.Chart.Add(retained);
        await ((Func<Task>)(() => inProgress.Sut.CloseFiscalYearAsync(January, retained.Id, "actor", Ct)))
            .Should().ThrowAsync<FiscalYearClosingAlreadyInProgressException>();

        var completedUnknown = new Fixture { PostingRows = [Posting(January, PostingStateStatus.Completed)] };
        completedUnknown.Chart.Add(retained);
        await ((Func<Task>)(() => completedUnknown.Sut.CloseFiscalYearAsync(January, retained.Id, "actor", Ct)))
            .Should().ThrowAsync<FiscalYearAlreadyClosedException>();

        var completedSame = new Fixture
        {
            PostingRows = [Posting(January, PostingStateStatus.Completed)],
            AuditEvents = [FiscalAudit(retained.Id)]
        };
        completedSame.Chart.Add(retained);
        await ((Func<Task>)(() => completedSame.Sut.CloseFiscalYearAsync(January, retained.Id, "actor", Ct)))
            .Should().ThrowAsync<FiscalYearAlreadyClosedException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CloseFiscalYearCompletedWithDifferentAccountReportsActiveResolvedOrMissingDisplay(int lookup)
    {
        var requested = Equity("300");
        var actual = Equity("301");
        var f = new Fixture
        {
            PostingRows = [Posting(January, PostingStateStatus.Completed)],
            AuditEvents = [FiscalAudit(actual.Id)]
        };
        f.Chart.Add(requested);
        if (lookup == 0)
            f.Chart.Add(actual);
        else if (lookup == 1)
            f.ResolvedAccount = actual;

        var error = await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(January, requested.Id, "actor", Ct)))
            .Should().ThrowAsync<FiscalYearAlreadyClosedWithDifferentRetainedEarningsException>();

        error.Which.ActualRetainedEarningsAccountId.Should().Be(actual.Id);
        error.Which.ActualRetainedEarningsAccountDisplay.Should().Be(
            lookup == 2 ? null : "301 — Equity");
    }

    [Theory]
    [InlineData(PostingStateBeginResult.AlreadyCompleted)]
    [InlineData(PostingStateBeginResult.InProgress)]
    public async Task CloseFiscalYearWithoutMovementsTranslatesPostingStateBeginResults(PostingStateBeginResult begin)
    {
        var retained = Equity("300");
        var asset = new Account(Guid.NewGuid(), "100", "Asset", AccountType.Asset, StatementSection.Assets);
        var zeroIncome = Account("400", AccountType.Income, StatementSection.Income);
        var zeroExpense = Account("500", AccountType.Expense, StatementSection.Expenses);
        var f = new Fixture
        {
            BeginResult = begin,
            TrialBalanceRows =
            [
                new TrialBalanceRow { AccountId = Guid.Empty, ClosingBalance = 100m },
                new TrialBalanceRow { AccountId = asset.Id, ClosingBalance = 0m },
                new TrialBalanceRow { AccountId = zeroIncome.Id, ClosingBalance = 0m },
                new TrialBalanceRow { AccountId = zeroExpense.Id, ClosingBalance = 0m }
            ]
        };
        f.Chart.Add(retained);
        f.Chart.Add(asset);
        f.Chart.Add(zeroIncome);
        f.Chart.Add(zeroExpense);

        Func<Task> action = () => f.Sut.CloseFiscalYearAsync(January, retained.Id, "actor", Ct);
        if (begin == PostingStateBeginResult.AlreadyCompleted)
            await action.Should().ThrowAsync<FiscalYearAlreadyClosedException>();
        else
            await action.Should().ThrowAsync<FiscalYearClosingAlreadyInProgressException>();
    }

    [Fact]
    public async Task CloseFiscalYearRejectsUnresolvableInactiveTrialBalanceAccount()
    {
        var retained = Equity("300");
        var missingId = Guid.NewGuid();
        var f = new Fixture
        {
            TrialBalanceRows = [new TrialBalanceRow { AccountId = missingId, ClosingBalance = 10m }]
        };
        f.Chart.Add(retained);

        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(January, retained.Id, "actor", Ct)))
            .Should().ThrowAsync<AccountNotFoundException>();
        f.AccountResolver.Verify(x => x.GetByIdsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => new HashSet<Guid>(ids).SetEquals(new[] { missingId })), Ct), Times.Once);
    }

    [Fact]
    public async Task CloseFiscalYearPostsEveryProfitAndLossSectionBothDirectionsAndSkipsOtherRows()
    {
        var retained = Equity("300");
        var income = Account("400", AccountType.Income, StatementSection.Income);
        var expense = Account("500", AccountType.Expense, StatementSection.Expenses);
        var otherIncome = Account("410", AccountType.Income, StatementSection.OtherIncome);
        var otherExpense = Account("510", AccountType.Expense, StatementSection.OtherExpense);
        var cogs = Account("520", AccountType.Expense, StatementSection.CostOfGoodsSold);
        var asset = Account("100", AccountType.Asset, StatementSection.Assets);
        var f = new Fixture
        {
            TrialBalanceRows =
            [
                new TrialBalanceRow { AccountId = income.Id, ClosingBalance = -10m },
                new TrialBalanceRow { AccountId = expense.Id, ClosingBalance = 20m },
                new TrialBalanceRow { AccountId = otherIncome.Id, ClosingBalance = -30m },
                new TrialBalanceRow { AccountId = otherExpense.Id, ClosingBalance = 40m },
                new TrialBalanceRow { AccountId = cogs.Id, ClosingBalance = 50m },
                new TrialBalanceRow { AccountId = Guid.Empty, ClosingBalance = 60m },
                new TrialBalanceRow { AccountId = asset.Id, ClosingBalance = 70m },
                new TrialBalanceRow { AccountId = expense.Id, ClosingBalance = 0m }
            ]
        };
        foreach (var account in new[] { retained, income, expense, otherIncome, otherExpense, cogs, asset })
            f.Chart.Add(account);

        await f.Sut.CloseFiscalYearAsync(January, retained.Id, "actor", Ct);

        f.EntryWriter.Verify(x => x.WriteAsync(
            It.Is<IReadOnlyList<AccountingEntry>>(entries => entries.Count == 5
                && entries.Any(e => e.Debit.Id == income.Id && e.Credit.Id == retained.Id && e.Amount == 10m)
                && entries.Any(e => e.Debit.Id == retained.Id && e.Credit.Id == expense.Id && e.Amount == 20m)),
            Ct), Times.Once);
        f.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Period, It.IsAny<Guid>(), AuditActionCodes.PeriodCloseFiscalYear,
            It.Is<IReadOnlyList<AuditFieldChange>>(changes => changes.Any(c =>
                c.FieldPath == "closing_entries_posted" && c.NewValueJson == "true")),
            It.IsAny<object>(), It.IsAny<Guid?>(), Ct), Times.Once);
    }

    [Fact]
    public async Task CloseFiscalYearUsesResolvedInactiveAccountAndMapsPostingEngineOutcomes()
    {
        var retained = Equity("300");
        var inactive = Account("499", AccountType.Income, StatementSection.Income);

        var already = new Fixture
        {
            BeginResult = PostingStateBeginResult.AlreadyCompleted,
            TrialBalanceRows = [new TrialBalanceRow { AccountId = inactive.Id, ClosingBalance = -1m }],
            ResolvedAccounts = new Dictionary<Guid, Account> { [inactive.Id] = inactive }
        };
        already.Chart.Add(retained);
        await ((Func<Task>)(() => already.Sut.CloseFiscalYearAsync(January, retained.Id, "actor", Ct)))
            .Should().ThrowAsync<FiscalYearAlreadyClosedException>();

        var inProgress = new Fixture
        {
            BeginResult = PostingStateBeginResult.InProgress,
            TrialBalanceRows = [new TrialBalanceRow { AccountId = inactive.Id, ClosingBalance = -1m }],
            ResolvedAccounts = new Dictionary<Guid, Account> { [inactive.Id] = inactive }
        };
        inProgress.Chart.Add(retained);
        await ((Func<Task>)(() => inProgress.Sut.CloseFiscalYearAsync(January, retained.Id, "actor", Ct)))
            .Should().ThrowAsync<FiscalYearClosingAlreadyInProgressException>();
    }

    [Fact]
    public async Task CloseFiscalYearDetectsAccountThatAppearsOnlyAfterPreflightBreak()
    {
        var retained = Equity("300");
        var income = Account("400", AccountType.Income, StatementSection.Income);
        var missing = Guid.NewGuid();
        var f = new Fixture
        {
            TrialBalanceRows =
            [
                new TrialBalanceRow { AccountId = income.Id, ClosingBalance = -1m },
                new TrialBalanceRow { AccountId = missing, ClosingBalance = 1m }
            ]
        };
        f.Chart.Add(retained);
        f.Chart.Add(income);

        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(January, retained.Id, "actor", Ct)))
            .Should().ThrowAsync<AccountNotFoundException>();
    }

    private static ClosedPeriodRecord Closed(DateOnly period) => new()
    {
        Period = period,
        ClosedBy = "tester",
        ClosedAtUtc = Fixture.Now
    };

    private static PostingStateRecord Posting(DateOnly period, PostingStateStatus status)
        => new(DeterministicGuid.Create($"CloseFiscalYear|{period:yyyy-MM-dd}"),
            PostingOperation.CloseFiscalYear, Fixture.Now, Fixture.Now, status, TimeSpan.Zero, TimeSpan.Zero);

    private static Account Equity(string code)
        => new(Guid.NewGuid(), code, "Equity", AccountType.Equity, StatementSection.Equity);

    private static Account Account(string code, AccountType type, StatementSection section)
        => new(Guid.NewGuid(), code, code, type, section, negativeBalancePolicy: NegativeBalancePolicy.Allow);

    private static AuditEvent FiscalAudit(Guid retainedEarningsAccountId)
        => new(
            Guid.NewGuid(), AuditEntityKind.Period, DeterministicGuid.Create("CloseFiscalYear|2026-01-01"),
            AuditActionCodes.PeriodCloseFiscalYear, null, Fixture.Now, null, null,
            [new AuditFieldChange("retained_earnings_account_id", null,
                System.Text.Json.JsonSerializer.Serialize(retainedEarningsAccountId))]);

    private sealed class Fixture
    {
        public static readonly DateTime Now = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IAuditLogService> Audit { get; } = new();
        public Mock<IAdvisoryLockManager> Locks { get; } = new();
        public Mock<IAccountingTurnoverReader> TurnoverReader { get; } = new();
        public Mock<IAccountingTurnoverAggregationReader> TurnoverAggregation { get; } = new();
        public Mock<IAccountingTurnoverWriter> TurnoverWriter { get; } = new();
        public Mock<IAccountingBalanceReader> BalanceReader { get; } = new();
        public Mock<IAccountingBalanceWriter> BalanceWriter { get; } = new();
        public Mock<IAccountingEntryMaintenanceWriter> EntryMaintenance { get; } = new();
        public Mock<IClosedPeriodRepository> ClosedPeriods { get; } = new();
        public Mock<IClosedPeriodReader> ClosedReader { get; } = new();
        public Mock<IAccountingPeriodActivityReader> ActivityReader { get; } = new();
        public Mock<IChartOfAccountsProvider> ChartProvider { get; } = new();
        public Mock<ITrialBalanceReader> TrialBalance { get; } = new();
        public Mock<IAccountingIntegrityChecker> Integrity { get; } = new();
        public Mock<IPostingStateRepository> PostingState { get; } = new();
        public Mock<IPostingStateReader> PostingReader { get; } = new();
        public Mock<IAuditEventReader> AuditReader { get; } = new();
        public Mock<IAccountByIdResolver> AccountResolver { get; } = new();
        public Mock<IAccountingEntryWriter> EntryWriter { get; } = new();
        public ChartOfAccounts Chart { get; } = new();

        public bool IsClosed { get; set; }
        public Func<DateOnly, bool>? IsClosedQuery { get; set; }
        public DateOnly? EarliestActivity { get; set; }
        public DateOnly? LatestClosed { get; set; }
        public IReadOnlyList<ClosedPeriodRecord> ClosedRows { get; set; } = [];
        public IReadOnlyList<PostingStateRecord> PostingRows { get; set; } = [];
        public IReadOnlyList<AccountingTurnover> Turnovers { get; set; } = [];
        public IReadOnlyList<AccountingBalance> PreviousBalances { get; set; } = [];
        public IReadOnlyList<TrialBalanceRow> TrialBalanceRows { get; set; } = [];
        public IReadOnlyList<DateOnly> DeletedEntryPeriods { get; set; } = [];
        public IReadOnlyList<AccountingTurnover> AggregatedTurnovers { get; set; } = [];
        public IReadOnlyDictionary<Guid, Account> ResolvedAccounts { get; set; }
            = new Dictionary<Guid, Account>();
        public Account? ResolvedAccount { get; set; }
        public PostingStateBeginResult BeginResult { get; set; } = PostingStateBeginResult.Begun;
        public IReadOnlyList<AuditEvent> AuditEvents { get; set; } = [];

        public PeriodClosingService Sut { get; }

        public Fixture()
        {
            var activeTransaction = false;
            Uow.SetupGet(x => x.HasActiveTransaction).Returns(() => activeTransaction);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .Callback(() => activeTransaction = true).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Callback(() => activeTransaction = false).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>()))
                .Callback(() => activeTransaction = false).Returns(Task.CompletedTask);

            Audit.Setup(x => x.WriteAsync(
                    It.IsAny<AuditEntityKind>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<AuditFieldChange>>(), It.IsAny<object>(), It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Locks.Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            TurnoverReader.Setup(x => x.GetForPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Turnovers);
            TurnoverAggregation.Setup(x => x.GetAggregatedFromRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => AggregatedTurnovers);
            TurnoverWriter.Setup(x => x.DeleteForPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            TurnoverWriter.Setup(x => x.WriteAsync(It.IsAny<IEnumerable<AccountingTurnover>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            BalanceReader.Setup(x => x.GetForPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => PreviousBalances);
            BalanceWriter.Setup(x => x.SaveAsync(It.IsAny<IEnumerable<AccountingBalance>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            BalanceWriter.Setup(x => x.DeleteForPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            EntryMaintenance.Setup(x => x.DeleteByDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => DeletedEntryPeriods);
            ClosedPeriods.Setup(x => x.IsClosedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly period, CancellationToken _) => IsClosedQuery?.Invoke(period) ?? IsClosed);
            ClosedPeriods.Setup(x => x.MarkClosedAsync(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            ClosedPeriods.Setup(x => x.ReopenAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            ClosedReader.Setup(x => x.GetLatestClosedPeriodAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => LatestClosed);
            ClosedReader.Setup(x => x.GetClosedAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly from, DateOnly to, CancellationToken _) =>
                    (IReadOnlyList<ClosedPeriodRecord>)ClosedRows.Where(x => x.Period >= from && x.Period <= to).ToArray());
            ActivityReader.Setup(x => x.GetEarliestActivityPeriodAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => EarliestActivity);
            ChartProvider.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Chart);
            TrialBalance.Setup(x => x.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => TrialBalanceRows);
            Integrity.Setup(x => x.AssertPeriodIsBalancedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            PostingState.Setup(x => x.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<PostingOperation>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => BeginResult);
            PostingState.Setup(x => x.MarkCompletedAsync(It.IsAny<Guid>(), It.IsAny<PostingOperation>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            PostingState.Setup(x => x.ClearCompletedStateAsync(It.IsAny<Guid>(), It.IsAny<PostingOperation>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            PostingReader.Setup(x => x.GetPageAsync(It.IsAny<PostingStatePageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PostingStatePageRequest request, CancellationToken _) => new PostingStatePage(
                    PostingRows.Where(x => x.DocumentId == request.DocumentId && x.Operation == request.Operation).ToArray(),
                    false, null));
            AuditReader.Setup(x => x.QueryAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => AuditEvents);
            AccountResolver.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ResolvedAccounts);
            AccountResolver.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ResolvedAccount);
            EntryWriter.Setup(x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingEntry>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var contextFactory = new Mock<IAccountingPostingContextFactory>();
            contextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new AccountingPostingContext(Chart));
            var dimensions = new Mock<IDimensionSetService>();
            dimensions.Setup(x => x.GetOrCreateIdsAsync(
                    It.IsAny<IReadOnlyList<NGB.Core.Dimensions.DimensionBag>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<NGB.Core.Dimensions.DimensionBag> bags, CancellationToken _) =>
                    bags.Select(static _ => Guid.NewGuid()).ToArray());
            var operational = new Mock<IAccountingOperationalBalanceReader>();
            operational.Setup(x => x.GetForKeysAsync(It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<AccountingBalanceKey>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            var validator = new Mock<IAccountingPostingValidator>();
            var engine = new PostingEngine(
                contextFactory.Object, Uow.Object, Locks.Object, EntryWriter.Object, TurnoverWriter.Object,
                dimensions.Object, operational.Object, ClosedPeriods.Object, validator.Object, PostingState.Object,
                new Mock<ILogger<PostingEngine>>().Object, new FixedTimeProvider(Now));

            Sut = new PeriodClosingService(
                Uow.Object, Audit.Object, Locks.Object, TurnoverReader.Object, TurnoverAggregation.Object,
                TurnoverWriter.Object, BalanceReader.Object, BalanceWriter.Object, EntryMaintenance.Object,
                ClosedPeriods.Object, ClosedReader.Object, ActivityReader.Object, ChartProvider.Object,
                TrialBalance.Object, engine, new AccountingBalanceCalculator(), Integrity.Object,
                PostingState.Object, PostingReader.Object, AuditReader.Object,
                new AccountingNegativeBalanceChecker(ChartProvider.Object), AccountResolver.Object,
                new Mock<ILogger<PeriodClosingService>>().Object, new FixedTimeProvider(Now));
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
