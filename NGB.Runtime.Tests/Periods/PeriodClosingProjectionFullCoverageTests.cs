using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Contracts.Accounting;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Readers.Accounts;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.CurrentActor;
using NGB.Runtime.Periods;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.Tests.Periods;

public sealed class PeriodClosingProjectionFullCoverageTests
{
    private static readonly CancellationToken Ct = new CancellationTokenSource().Token;

    [Fact]
    public async Task PublicCommandsRejectNullRequestsAndCalendarRejectsBothYearBoundaries()
    {
        var f = new Fixture();

        await ((Func<Task>)(() => f.Sut.CloseMonthAsync(null!, Ct))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.ReopenMonthAsync(null!, Ct))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.CloseFiscalYearAsync(null!, Ct))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.ReopenFiscalYearAsync(null!, Ct))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetCalendarAsync(1899, Ct))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.GetCalendarAsync(10000, Ct))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.GetFiscalYearStatusAsync(new DateOnly(2026, 1, 2), Ct)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task CommandsResolveActorDisplayAndReturnRefreshedStatuses()
    {
        var f = new Fixture();
        var period = new DateOnly(2026, 1, 17);

        f.Actor = new ActorIdentity(" subject ", " email@example.test ", " Display Name ");
        (await f.Sut.CloseMonthAsync(new CloseMonthRequestDto(period), Ct)).Period.Should().Be(new DateOnly(2026, 1, 1));
        f.Closing.Verify(x => x.CloseMonthAsync(period, "Display Name", Ct), Times.Once);

        f.Actor = new ActorIdentity(" subject ", " email@example.test ", " ");
        await f.Sut.ReopenMonthAsync(new ReopenMonthRequestDto(period, "reason"), Ct);
        f.Closing.Verify(x => x.ReopenMonthAsync(period, "email@example.test", "reason", Ct), Times.Once);

        f.Actor = new ActorIdentity(" subject ", " ", null);
        await f.Sut.CloseFiscalYearAsync(new CloseFiscalYearRequestDto(new DateOnly(2026, 1, 1), Guid.NewGuid()), Ct);
        f.Closing.Verify(x => x.CloseFiscalYearAsync(
            new DateOnly(2026, 1, 1), It.IsAny<Guid>(), "subject", Ct), Times.Once);

        await f.Sut.ReopenFiscalYearAsync(new ReopenFiscalYearRequestDto(new DateOnly(2026, 1, 1), "reason"), Ct);
        f.Closing.Verify(x => x.ReopenFiscalYearAsync(new DateOnly(2026, 1, 1), "subject", "reason", Ct), Times.Once);

        f.Actor = null;
        await ((Func<Task>)(() => f.Sut.CloseMonthAsync(new CloseMonthRequestDto(period), Ct)))
            .Should().ThrowAsync<PeriodClosingCurrentActorRequiredException>();
    }

    [Fact]
    public async Task CalendarAndRetainedEarningsSearchProjectCompleteDtos()
    {
        var f = new Fixture
        {
            EarliestActivity = new DateOnly(2026, 1, 1),
            LatestClosed = new DateOnly(2026, 1, 1),
            ClosedRows = [Closed(new DateOnly(2026, 1, 1))],
            ActivityPeriods = [new DateOnly(2026, 2, 1)],
            LookupRows =
            [
                new AccountLookupRecord { AccountId = Guid.NewGuid(), Code = "300", Name = "Retained" }
            ]
        };

        var calendar = await f.Sut.GetCalendarAsync(2026, Ct);
        calendar.Months.Should().HaveCount(12);
        calendar.YearStartPeriod.Should().Be(new DateOnly(2026, 1, 1));
        calendar.YearEndPeriod.Should().Be(new DateOnly(2026, 12, 1));
        calendar.Months[0].IsClosed.Should().BeTrue();
        calendar.Months[1].HasActivity.Should().BeTrue();

        var options = await f.Sut.SearchRetainedEarningsAccountsAsync("ret", 7, Ct);
        options.Should().ContainSingle().Which.Display.Should().Be("300 — Retained");
        f.RetainedLookup.Verify(x => x.SearchAsync("ret", 7, Ct), Times.Once);
    }

    [Fact]
    public async Task MonthStatusesCoverClosedNormalFiscalBlockedAndNotLatestCases()
    {
        var period = new DateOnly(2026, 3, 1);
        var f = new Fixture
        {
            EarliestActivity = period,
            LatestClosed = period,
            ClosedRows = [Closed(period)],
            ActivityPeriods = [period]
        };

        var normal = await f.Sut.GetMonthStatusAsync(period.AddDays(12), Ct);
        normal.State.Should().Be("Closed");
        normal.CanReopen.Should().BeTrue();
        normal.HasActivity.Should().BeTrue();
        normal.ClosedBy.Should().Be("tester");

        f.PostingRows = [Posting(period, PostingStateStatus.Completed)];
        var fiscalBlocked = await f.Sut.GetMonthStatusAsync(period, Ct);
        fiscalBlocked.State.Should().Be("Closed");
        fiscalBlocked.CanReopen.Should().BeFalse();
        fiscalBlocked.BlockingReason.Should().Be("FiscalYearClose");
        fiscalBlocked.BlockingPeriod.Should().Be(period);

        var next = period.AddMonths(1);
        f.LatestClosed = next;
        f.ClosedRows = [Closed(period), Closed(next)];
        f.PostingRows = [];
        var notLatest = await f.Sut.GetMonthStatusAsync(period, Ct);
        notLatest.State.Should().Be("Closed");
        notLatest.CanReopen.Should().BeFalse();
    }

    [Fact]
    public async Task MonthStatusesCoverOutOfSequenceAndEveryOpenState()
    {
        var january = new DateOnly(2026, 1, 1);
        var february = january.AddMonths(1);
        var march = january.AddMonths(2);
        var f = new Fixture
        {
            EarliestActivity = january,
            LatestClosed = march,
            ClosedRows = [Closed(january), Closed(march)]
        };

        var outOfSequence = await f.Sut.GetMonthStatusAsync(march, Ct);
        outOfSequence.State.Should().Be("ClosedOutOfSequence");
        outOfSequence.BlockingPeriod.Should().Be(february);
        outOfSequence.BlockingReason.Should().Be("LaterClosedMonths");
        outOfSequence.CanReopen.Should().BeTrue();

        var broken = await f.Sut.GetMonthStatusAsync(february, Ct);
        broken.State.Should().Be("BlockedByLaterClosedMonths");
        broken.BlockingPeriod.Should().Be(march);

        f.EarliestActivity = null;
        f.LatestClosed = null;
        f.ClosedRows = [];
        (await f.Sut.GetMonthStatusAsync(february, Ct)).State.Should().Be("Open");

        f.EarliestActivity = march;
        (await f.Sut.GetMonthStatusAsync(february, Ct)).State.Should().Be("Open");
        (await f.Sut.GetMonthStatusAsync(march, Ct)).State.Should().Be("ReadyToClose");

        f.EarliestActivity = january;
        f.LatestClosed = january;
        f.ClosedRows = [Closed(january)];
        var earlierOpen = await f.Sut.GetMonthStatusAsync(march, Ct);
        earlierOpen.State.Should().Be("BlockedByEarlierOpenMonth");
        earlierOpen.BlockingPeriod.Should().Be(february);
        earlierOpen.BlockingReason.Should().Be("EarlierOpenMonth");
    }

    [Fact]
    public async Task FiscalYearStatusesCoverCompletedInProgressStaleAndClosedEndPeriod()
    {
        var end = new DateOnly(2026, 1, 1);
        var f = new Fixture { PostingRows = [Posting(end, PostingStateStatus.Completed)] };

        var completed = await f.Sut.GetFiscalYearStatusAsync(end, Ct);
        completed.State.Should().Be("Completed");
        completed.CanReopen.Should().BeTrue();
        completed.ReopenWillOpenEndPeriod.Should().BeFalse();

        f.ClosedRows = [Closed(end)];
        f.EarliestActivity = end;
        f.LatestClosed = end;
        completed = await f.Sut.GetFiscalYearStatusAsync(end, Ct);
        completed.ReopenWillOpenEndPeriod.Should().BeTrue();
        completed.EndPeriodClosed.Should().BeTrue();

        f.LatestClosed = end.AddMonths(1);
        f.ClosedRows = [Closed(end), Closed(end.AddMonths(1))];
        completed = await f.Sut.GetFiscalYearStatusAsync(end, Ct);
        completed.CanReopen.Should().BeFalse();
        completed.ReopenBlockingPeriod.Should().Be(end.AddMonths(1));
        completed.ReopenBlockingReason.Should().Be("LaterClosedMonths");

        f.ClosedRows = [];
        f.EarliestActivity = null;
        f.LatestClosed = null;
        f.PostingRows = [Posting(end, PostingStateStatus.InProgress)];
        (await f.Sut.GetFiscalYearStatusAsync(end, Ct)).State.Should().Be("InProgress");

        f.PostingRows = [Posting(end, PostingStateStatus.StaleInProgress)];
        (await f.Sut.GetFiscalYearStatusAsync(end, Ct)).State.Should().Be("StaleInProgress");

        f.PostingRows = [];
        f.ClosedRows = [Closed(end)];
        f.EarliestActivity = end;
        f.LatestClosed = end;
        var blocked = await f.Sut.GetFiscalYearStatusAsync(end, Ct);
        blocked.State.Should().Be("BlockedByClosedEndPeriod");
        blocked.BlockingReason.Should().Be("ClosedEndPeriod");
    }

    [Fact]
    public async Task FiscalYearStatusesCoverBrokenLaterEarlierAndReadyBranches()
    {
        var january = new DateOnly(2026, 1, 1);
        var march = january.AddMonths(2);
        var april = january.AddMonths(3);
        var f = new Fixture
        {
            EarliestActivity = january,
            LatestClosed = april,
            ClosedRows = [Closed(january), Closed(april)]
        };

        var broken = await f.Sut.GetFiscalYearStatusAsync(march, Ct);
        broken.State.Should().Be("BlockedByLaterClosedMonths");
        broken.BlockingPeriod.Should().Be(april);

        f.ClosedQuery = (from, to) => to == april
            ? [Closed(january), Closed(january.AddMonths(1)), Closed(march), Closed(april)]
            : [];
        var later = await f.Sut.GetFiscalYearStatusAsync(march, Ct);
        later.State.Should().Be("BlockedByLaterClosedMonths");
        later.BlockingPeriod.Should().Be(april);

        f.ClosedQuery = null;
        f.EarliestActivity = null;
        f.LatestClosed = null;
        f.ClosedRows = [];
        var earlier = await f.Sut.GetFiscalYearStatusAsync(march, Ct);
        earlier.State.Should().Be("BlockedByEarlierOpenMonth");
        earlier.BlockingPeriod.Should().Be(january);

        f.EarliestActivity = january;
        f.ClosedRows = [Closed(january), Closed(january.AddMonths(1))];
        f.ClosedQuery = (_, to) => to == march ? f.ClosedRows : [];
        earlier = await f.Sut.GetFiscalYearStatusAsync(march, Ct);
        earlier.State.Should().Be("BlockedByEarlierOpenMonth");
        earlier.BlockingPeriod.Should().Be(january);

        f.ClosedQuery = null;
        f.EarliestActivity = null;
        f.ClosedRows = [];
        var ready = await f.Sut.GetFiscalYearStatusAsync(january, Ct);
        ready.State.Should().Be("Ready");
        ready.CanClose.Should().BeTrue();

        f.EarliestActivity = march;
        f.LatestClosed = january;
        (await f.Sut.GetMonthStatusAsync(march, Ct)).State.Should().Be("ReadyToClose");

        f.EarliestActivity = null;
        f.LatestClosed = january;
        f.ClosedRows = [Closed(january)];
        (await f.Sut.GetMonthStatusAsync(january, Ct)).IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task CompletedFiscalYearProjectsArchivedAndActiveRetainedEarningsAccounts()
    {
        var end = new DateOnly(2026, 1, 1);
        var accountId = Guid.NewGuid();
        var f = new Fixture
        {
            PostingRows = [Posting(end, PostingStateStatus.Completed)],
            AuditEvents = [FiscalAudit(end, JsonSerializer.Serialize(accountId))]
        };

        var archived = await f.Sut.GetFiscalYearStatusAsync(end, Ct);
        archived.ClosedRetainedEarningsAccount.Should().NotBeNull();
        archived.ClosedRetainedEarningsAccount!.Name.Should().Be("Archived account");
        archived.ClosedRetainedEarningsAccount.Code.Should().Be(accountId.ToString());

        f.Account = new Account(accountId, "300", "Retained", AccountType.Equity);
        var active = await f.Sut.GetFiscalYearStatusAsync(end, Ct);
        active.ClosedRetainedEarningsAccount!.Display.Should().Be("300 — Retained");

        f.AuditEvents = [FiscalAudit(end, JsonSerializer.Serialize(Guid.Empty))];
        (await f.Sut.GetFiscalYearStatusAsync(end, Ct)).ClosedRetainedEarningsAccount.Should().BeNull();
    }

    [Fact]
    public async Task FiscalYearAuditReaderCoversMissingNullGuidAndInvalidJsonValues()
    {
        var reader = new Mock<IAuditEventReader>();
        var documentId = Guid.NewGuid();
        IReadOnlyList<AuditEvent> events = [];
        reader.Setup(x => x.QueryAsync(It.IsAny<AuditLogQuery>(), Ct)).ReturnsAsync(() => events);

        (await FiscalYearCloseAuditReader.TryGetRetainedEarningsAccountIdAsync(reader.Object, documentId, Ct))
            .Should().BeNull();
        reader.Verify(x => x.QueryAsync(It.Is<AuditLogQuery>(q =>
            q.EntityId == documentId && q.Limit == 1 && q.Offset == 0), Ct), Times.Once);

        FiscalYearCloseAuditReader.TryGetRetainedEarningsAccountId(FiscalAudit(documentId, null)).Should().BeNull();
        var unrelated = FiscalAudit(documentId, null) with
        {
            Changes = [new AuditFieldChange("other", null, "null")]
        };
        FiscalYearCloseAuditReader.TryGetRetainedEarningsAccountId(unrelated).Should().BeNull();
        FiscalYearCloseAuditReader.TryGetRetainedEarningsAccountId(FiscalAudit(documentId, " ")).Should().BeNull();
        FiscalYearCloseAuditReader.TryGetRetainedEarningsAccountId(FiscalAudit(documentId, "null")).Should().BeNull();
        FiscalYearCloseAuditReader.TryGetRetainedEarningsAccountId(FiscalAudit(documentId, "not-json")).Should().BeNull();

        var expected = Guid.NewGuid();
        events = [FiscalAudit(documentId, JsonSerializer.Serialize(expected))];
        (await FiscalYearCloseAuditReader.TryGetRetainedEarningsAccountIdAsync(reader.Object, documentId, Ct))
            .Should().Be(expected);
    }

    [Fact]
    public void ChainEvaluatorCoversEmptyBeforeBrokenContiguousAndSyntheticEdgeSnapshots()
    {
        var january = new DateOnly(2026, 1, 1);
        var february = january.AddMonths(1);
        var march = january.AddMonths(2);

        var empty = PeriodClosingChainEvaluator.Build(null, null, []);
        empty.CanCloseAnyMonth.Should().BeTrue();
        PeriodClosingChainEvaluator.CanCloseMonth(empty, january).Should().BeTrue();

        var noClosed = PeriodClosingChainEvaluator.Build(january, null, []);
        noClosed.NextClosablePeriod.Should().Be(january);
        PeriodClosingChainEvaluator.CanCloseMonth(noClosed, january).Should().BeTrue();
        PeriodClosingChainEvaluator.CanCloseMonth(noClosed, decemberBefore(january)).Should().BeTrue();
        PeriodClosingChainEvaluator.HasLaterClosedPeriods(noClosed, january).Should().BeFalse();

        var latestWithoutActivity = PeriodClosingChainEvaluator.Build(null, january, [january]);
        latestWithoutActivity.ChainStartPeriod.Should().Be(january);

        var activityAfterLatest = PeriodClosingChainEvaluator.Build(march, january, []);
        activityAfterLatest.NextClosablePeriod.Should().Be(march);

        var contiguous = PeriodClosingChainEvaluator.Build(january, february, [january, february]);
        contiguous.LatestContiguousClosedPeriod.Should().Be(february);
        contiguous.NextClosablePeriod.Should().Be(march);
        PeriodClosingChainEvaluator.HasLaterClosedPeriods(contiguous, february).Should().BeFalse();
        PeriodClosingChainEvaluator.HasLaterClosedPeriods(contiguous, january).Should().BeTrue();

        var broken = PeriodClosingChainEvaluator.Build(january, march, [january, march]);
        broken.FirstGapPeriod.Should().Be(february);
        PeriodClosingChainEvaluator.CanCloseMonth(broken, february).Should().BeFalse();
        PeriodClosingChainEvaluator.IsClosedOutOfSequence(broken, march).Should().BeTrue();
        PeriodClosingChainEvaluator.IsClosedOutOfSequence(broken, january).Should().BeFalse();

        var synthetic = new PeriodClosingChainSnapshot(null, null, january, january, january, false, false, null);
        PeriodClosingChainEvaluator.IsBeforeChainStart(synthetic, january).Should().BeFalse();
        PeriodClosingChainEvaluator.HasLaterClosedPeriods(synthetic, decemberBefore(january)).Should().BeFalse();
        PeriodClosingChainEvaluator.CanCloseMonth(synthetic, february).Should().BeFalse();
        PeriodClosingChainEvaluator.CanCloseMonth(synthetic with { NextClosablePeriod = null }, february).Should().BeFalse();
    }

    private static DateOnly decemberBefore(DateOnly january) => january.AddMonths(-1);

    private static ClosedPeriodRecord Closed(DateOnly period) => new()
    {
        Period = period,
        ClosedBy = "tester",
        ClosedAtUtc = new DateTime(period.Year, period.Month, 2, 3, 4, 5, DateTimeKind.Utc)
    };

    private static PostingStateRecord Posting(DateOnly period, PostingStateStatus status)
        => new(
            DeterministicGuid.Create($"CloseFiscalYear|{period:yyyy-MM-dd}"),
            PostingOperation.CloseFiscalYear,
            new DateTime(period.Year, period.Month, 1, 1, 2, 3, DateTimeKind.Utc),
            status == PostingStateStatus.Completed
                ? new DateTime(period.Year, period.Month, 1, 2, 3, 4, DateTimeKind.Utc)
                : null,
            status,
            null,
            TimeSpan.Zero);

    private static AuditEvent FiscalAudit(DateOnly period, string? value)
        => FiscalAudit(DeterministicGuid.Create($"CloseFiscalYear|{period:yyyy-MM-dd}"), value);

    private static AuditEvent FiscalAudit(Guid documentId, string? value)
        => new(
            Guid.NewGuid(),
            AuditEntityKind.Period,
            documentId,
            NGB.Runtime.AuditLog.AuditActionCodes.PeriodCloseFiscalYear,
            null,
            DateTime.UtcNow,
            null,
            null,
            [new AuditFieldChange("retained_earnings_account_id", null, value)]);

    private sealed class Fixture
    {
        public Mock<IPeriodClosingService> Closing { get; } = new();
        public Mock<ICurrentActorContext> CurrentActor { get; } = new();
        public Mock<IClosedPeriodReader> ClosedReader { get; } = new();
        public Mock<IAccountingPeriodActivityReader> ActivityReader { get; } = new();
        public Mock<IPostingStateReader> PostingReader { get; } = new();
        public Mock<IAuditEventReader> AuditReader { get; } = new();
        public Mock<IAccountByIdResolver> AccountResolver { get; } = new();
        public Mock<IRetainedEarningsAccountLookupReader> RetainedLookup { get; } = new();

        public ActorIdentity? Actor { get; set; } = new("subject", "actor@example.test", "Actor");
        public DateOnly? EarliestActivity { get; set; }
        public DateOnly? LatestClosed { get; set; }
        public IReadOnlyList<ClosedPeriodRecord> ClosedRows { get; set; } = [];
        public IReadOnlyList<DateOnly> ActivityPeriods { get; set; } = [];
        public IReadOnlyList<PostingStateRecord> PostingRows { get; set; } = [];
        public IReadOnlyList<AuditEvent> AuditEvents { get; set; } = [];
        public IReadOnlyList<AccountLookupRecord> LookupRows { get; set; } = [];
        public Account? Account { get; set; }
        public Func<DateOnly, DateOnly, IReadOnlyList<ClosedPeriodRecord>>? ClosedQuery { get; set; }

        public PeriodClosingUiService Sut { get; }

        public Fixture()
        {
            CurrentActor.SetupGet(x => x.Current).Returns(() => Actor);
            ActivityReader.Setup(x => x.GetEarliestActivityPeriodAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => EarliestActivity);
            ActivityReader.Setup(x => x.GetActivityPeriodsAsync(
                    It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly from, DateOnly to, CancellationToken _) =>
                    (IReadOnlyList<DateOnly>)ActivityPeriods.Where(x => x >= from && x <= to).ToArray());
            ClosedReader.Setup(x => x.GetLatestClosedPeriodAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => LatestClosed);
            ClosedReader.Setup(x => x.GetClosedAsync(
                    It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly from, DateOnly to, CancellationToken _) =>
                    ClosedQuery?.Invoke(from, to)
                    ?? ClosedRows.Where(x => x.Period >= from && x.Period <= to).ToArray());
            PostingReader.Setup(x => x.GetPageAsync(It.IsAny<PostingStatePageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PostingStatePageRequest request, CancellationToken _) => new PostingStatePage(
                    PostingRows.Where(x => x.DocumentId == request.DocumentId && x.Operation == request.Operation).ToArray(),
                    false,
                    null));
            AuditReader.Setup(x => x.QueryAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AuditLogQuery query, CancellationToken _) =>
                    (IReadOnlyList<AuditEvent>)AuditEvents.Where(x => x.EntityId == query.EntityId).ToArray());
            AccountResolver.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Account);
            RetainedLookup.Setup(x => x.SearchAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => LookupRows);

            Sut = new PeriodClosingUiService(
                Closing.Object,
                CurrentActor.Object,
                ClosedReader.Object,
                ActivityReader.Object,
                PostingReader.Object,
                AuditReader.Object,
                AccountResolver.Object,
                RetainedLookup.Object);
        }
    }
}
