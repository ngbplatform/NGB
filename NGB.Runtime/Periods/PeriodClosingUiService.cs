using NGB.Accounting.Periods;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Readers.Accounts;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Periods;

public sealed class PeriodClosingUiService(
    IPeriodClosingService closingService,
    ICurrentActorContext currentActorContext,
    IClosedPeriodReader closedPeriodReader,
    IAccountingPeriodActivityReader activityReader,
    IPostingStateReader postingStateReader,
    IAuditEventReader auditEventReader,
    IAccountByIdResolver accountByIdResolver,
    IRetainedEarningsAccountLookupReader retainedEarningsLookup)
    : IPeriodClosingUiService
{
    private const string MonthOpenState = "Open";
    private const string MonthClosedState = "Closed";
    private const string MonthClosedOutOfSequenceState = "ClosedOutOfSequence";
    private const string MonthReadyToCloseState = "ReadyToClose";
    private const string MonthBlockedByEarlierOpenMonthState = "BlockedByEarlierOpenMonth";
    private const string MonthBlockedByLaterClosedMonthsState = "BlockedByLaterClosedMonths";

    private const string BlockingEarlierOpenMonth = "EarlierOpenMonth";
    private const string BlockingLaterClosedMonths = "LaterClosedMonths";
    private const string BlockingFiscalYearClose = "FiscalYearClose";
    private const string BlockingClosedEndPeriod = "ClosedEndPeriod";

    private const string FiscalYearReadyState = "Ready";
    private const string FiscalYearCompletedState = "Completed";
    private const string FiscalYearInProgressState = "InProgress";
    private const string FiscalYearStaleInProgressState = "StaleInProgress";
    private const string FiscalYearBlockedByEarlierOpenMonthState = "BlockedByEarlierOpenMonth";
    private const string FiscalYearBlockedByLaterClosedMonthsState = "BlockedByLaterClosedMonths";
    private const string FiscalYearBlockedByClosedEndPeriodState = "BlockedByClosedEndPeriod";

    public async Task<PeriodCloseStatusDto> GetMonthStatusAsync(DateOnly period, CancellationToken ct)
    {
        var normalized = AccountingPeriod.FromDateOnly(period);
        var chain = await LoadChainSnapshotAsync(ct);
        var closedByPeriod = (await closedPeriodReader.GetClosedAsync(normalized, normalized, ct))
            .ToDictionary(x => x.Period, x => x);
        var activityPeriods = (await activityReader.GetActivityPeriodsAsync(normalized, normalized, ct)).ToHashSet();

        return await BuildMonthStatusAsync(normalized, chain, closedByPeriod, activityPeriods, ct);
    }

    public async Task<PeriodCloseStatusDto> CloseMonthAsync(CloseMonthRequestDto request, CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        await closingService.CloseMonthAsync(request.Period, ResolveCurrentActorDisplay(), ct);
        return await GetMonthStatusAsync(request.Period, ct);
    }

    public async Task<PeriodCloseStatusDto> ReopenMonthAsync(ReopenMonthRequestDto request, CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        await closingService.ReopenMonthAsync(request.Period, ResolveCurrentActorDisplay(), request.Reason, ct);
        return await GetMonthStatusAsync(request.Period, ct);
    }

    public async Task<PeriodClosingCalendarDto> GetCalendarAsync(int year, CancellationToken ct)
    {
        if (year is < 1900 or > 9999)
            throw new NgbArgumentOutOfRangeException(nameof(year), year, "Year is out of range.");

        var yearStartPeriod = new DateOnly(year, 1, 1);
        var yearEndPeriod = new DateOnly(year, 12, 1);
        var chain = await LoadChainSnapshotAsync(ct);

        var closedByPeriod = (await closedPeriodReader.GetClosedAsync(yearStartPeriod, yearEndPeriod, ct))
            .ToDictionary(x => x.Period, x => x);
        var activityPeriods = (await activityReader.GetActivityPeriodsAsync(yearStartPeriod, yearEndPeriod, ct)).ToHashSet();

        var months = new List<PeriodCloseStatusDto>(capacity: 12);
        for (var period = yearStartPeriod; period <= yearEndPeriod; period = period.AddMonths(1))
            months.Add(await BuildMonthStatusAsync(period, chain, closedByPeriod, activityPeriods, ct));

        return new PeriodClosingCalendarDto(
            Year: year,
            YearStartPeriod: yearStartPeriod,
            YearEndPeriod: yearEndPeriod,
            EarliestActivityPeriod: chain.EarliestActivityPeriod,
            LatestContiguousClosedPeriod: chain.LatestContiguousClosedPeriod,
            LatestClosedPeriod: chain.LatestClosedPeriod,
            NextClosablePeriod: chain.NextClosablePeriod,
            CanCloseAnyMonth: chain.CanCloseAnyMonth,
            HasBrokenChain: chain.HasBrokenChain,
            FirstGapPeriod: chain.FirstGapPeriod,
            Months: months);
    }

    public async Task<FiscalYearCloseStatusDto> GetFiscalYearStatusAsync(DateOnly fiscalYearEndPeriod, CancellationToken ct)
    {
        fiscalYearEndPeriod.EnsureMonthStart(nameof(fiscalYearEndPeriod));

        var chain = await LoadChainSnapshotAsync(ct);
        var fiscalYearStartPeriod = new DateOnly(fiscalYearEndPeriod.Year, 1, 1);
        var closedRows = await closedPeriodReader.GetClosedAsync(fiscalYearStartPeriod, fiscalYearEndPeriod, ct);
        var closedByPeriod = closedRows.ToDictionary(x => x.Period, x => x);
        var activityPeriods = (await activityReader.GetActivityPeriodsAsync(fiscalYearStartPeriod, fiscalYearEndPeriod, ct)).ToHashSet();

        var endPeriodClosed = closedByPeriod.GetValueOrDefault(fiscalYearEndPeriod);
        var priorMonths = new List<PeriodCloseStatusDto>();
        for (var period = fiscalYearStartPeriod; period < fiscalYearEndPeriod; period = period.AddMonths(1))
        {
            priorMonths.Add(await BuildMonthStatusAsync(period, chain, closedByPeriod, activityPeriods, ct));
        }

        var documentId = DeterministicGuid.Create($"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}");
        var postingRow = (await postingStateReader.GetPageAsync(
            new PostingStatePageRequest
            {
                DocumentId = documentId,
                Operation = PostingOperation.CloseFiscalYear,
                PageSize = 1
            },
            ct)).Records.SingleOrDefault();
        var closedRetainedEarningsAccount = await TryGetClosedRetainedEarningsAccountAsync(
            documentId,
            postingRow?.Status,
            ct);
        var canReopen = false;
        var reopenWillOpenEndPeriod = false;
        DateOnly? reopenBlockingPeriod = null;
        string? reopenBlockingReason = null;

        string state;
        DateOnly? blockingPeriod = null;
        string? blockingReason = null;

        if (postingRow?.Status == PostingStateStatus.Completed)
        {
            state = FiscalYearCompletedState;
            if (chain.LatestClosedPeriod is not null && chain.LatestClosedPeriod.Value > fiscalYearEndPeriod)
            {
                reopenBlockingPeriod = chain.LatestClosedPeriod;
                reopenBlockingReason = BlockingLaterClosedMonths;
            }
            else
            {
                canReopen = true;
                reopenWillOpenEndPeriod = endPeriodClosed is not null;
            }
        }
        else if (postingRow?.Status == PostingStateStatus.InProgress)
        {
            state = FiscalYearInProgressState;
        }
        else if (postingRow?.Status == PostingStateStatus.StaleInProgress)
        {
            state = FiscalYearStaleInProgressState;
        }
        else if (endPeriodClosed is not null)
        {
            state = FiscalYearBlockedByClosedEndPeriodState;
            blockingPeriod = fiscalYearEndPeriod;
            blockingReason = BlockingClosedEndPeriod;
        }
        else if (chain is { HasBrokenChain: true, NextClosablePeriod: not null } && chain.NextClosablePeriod.Value <= fiscalYearEndPeriod)
        {
            state = FiscalYearBlockedByLaterClosedMonthsState;
            blockingPeriod = chain.LatestClosedPeriod;
            blockingReason = BlockingLaterClosedMonths;
        }
        else if (chain.LatestClosedPeriod is not null
                 && chain.LatestClosedPeriod.Value > fiscalYearEndPeriod
                 && chain.ChainStartPeriod is not null
                 && fiscalYearEndPeriod >= chain.ChainStartPeriod.Value)
        {
            state = FiscalYearBlockedByLaterClosedMonthsState;
            blockingPeriod = chain.LatestClosedPeriod;
            blockingReason = BlockingLaterClosedMonths;
        }
        else
        {
            var firstOpenPriorMonth = priorMonths.FirstOrDefault(x => !x.IsClosed)?.Period;
            if (firstOpenPriorMonth is not null)
            {
                state = FiscalYearBlockedByEarlierOpenMonthState;
                blockingPeriod = firstOpenPriorMonth;
                blockingReason = BlockingEarlierOpenMonth;
            }
            else if (chain.NextClosablePeriod is not null && chain.NextClosablePeriod.Value < fiscalYearEndPeriod)
            {
                state = FiscalYearBlockedByEarlierOpenMonthState;
                blockingPeriod = chain.NextClosablePeriod;
                blockingReason = BlockingEarlierOpenMonth;
            }
            else
            {
                state = FiscalYearReadyState;
            }
        }

        return new FiscalYearCloseStatusDto(
            FiscalYearEndPeriod: fiscalYearEndPeriod,
            FiscalYearStartPeriod: fiscalYearStartPeriod,
            State: state,
            DocumentId: documentId,
            StartedAtUtc: postingRow?.StartedAtUtc,
            CompletedAtUtc: postingRow?.CompletedAtUtc,
            EndPeriodClosed: endPeriodClosed is not null,
            EndPeriodClosedBy: endPeriodClosed?.ClosedBy,
            EndPeriodClosedAtUtc: endPeriodClosed?.ClosedAtUtc,
            CanClose: state == FiscalYearReadyState,
            CanReopen: canReopen,
            ReopenWillOpenEndPeriod: reopenWillOpenEndPeriod,
            ClosedRetainedEarningsAccount: closedRetainedEarningsAccount,
            BlockingPeriod: blockingPeriod,
            BlockingReason: blockingReason,
            ReopenBlockingPeriod: reopenBlockingPeriod,
            ReopenBlockingReason: reopenBlockingReason,
            PriorMonths: priorMonths);
    }

    public async Task<FiscalYearCloseStatusDto> CloseFiscalYearAsync(
        CloseFiscalYearRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        await closingService.CloseFiscalYearAsync(
            request.FiscalYearEndPeriod,
            request.RetainedEarningsAccountId,
            ResolveCurrentActorDisplay(),
            ct);

        return await GetFiscalYearStatusAsync(request.FiscalYearEndPeriod, ct);
    }

    public async Task<FiscalYearCloseStatusDto> ReopenFiscalYearAsync(
        ReopenFiscalYearRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        await closingService.ReopenFiscalYearAsync(
            request.FiscalYearEndPeriod,
            ResolveCurrentActorDisplay(),
            request.Reason,
            ct);

        return await GetFiscalYearStatusAsync(request.FiscalYearEndPeriod, ct);
    }

    private string ResolveCurrentActorDisplay()
    {
        var actor = currentActorContext.Current;
        if (actor is null)
            throw new PeriodClosingCurrentActorRequiredException();

        var display = string.IsNullOrWhiteSpace(actor.DisplayName)
            ? string.IsNullOrWhiteSpace(actor.Email)
                ? actor.AuthSubject
                : actor.Email
            : actor.DisplayName;

        return display.Trim();
    }

    public async Task<IReadOnlyList<RetainedEarningsAccountOptionDto>> SearchRetainedEarningsAccountsAsync(
        string? query,
        int limit,
        CancellationToken ct)
    {
        var rows = await retainedEarningsLookup.SearchAsync(query, limit, ct);
        return rows
            .Select(x => new RetainedEarningsAccountOptionDto(
                AccountId: x.AccountId,
                Code: x.Code,
                Name: x.Name,
                Display: $"{x.Code} — {x.Name}"))
            .ToArray();
    }

    private async Task<RetainedEarningsAccountOptionDto?> TryGetClosedRetainedEarningsAccountAsync(
        Guid documentId,
        PostingStateStatus? status,
        CancellationToken ct)
    {
        if (status != PostingStateStatus.Completed)
            return null;

        var accountId = await FiscalYearCloseAuditReader.TryGetRetainedEarningsAccountIdAsync(auditEventReader, documentId, ct);
        if (!accountId.HasValue || accountId.Value == Guid.Empty)
            return null;

        var account = await accountByIdResolver.GetByIdAsync(accountId.Value, ct);
        if (account is null)
        {
            return new RetainedEarningsAccountOptionDto(
                AccountId: accountId.Value,
                Code: accountId.Value.ToString(),
                Name: "Archived account",
                Display: accountId.Value.ToString());
        }

        return new RetainedEarningsAccountOptionDto(
            AccountId: account.Id,
            Code: account.Code,
            Name: account.Name,
            Display: $"{account.Code} — {account.Name}");
    }

    private async Task<PeriodCloseStatusDto> BuildMonthStatusAsync(
        DateOnly period,
        PeriodClosingChainSnapshot chain,
        IReadOnlyDictionary<DateOnly, ClosedPeriodRecord> closedByPeriod,
        IReadOnlySet<DateOnly> activityPeriods,
        CancellationToken ct)
    {
        var row = closedByPeriod.GetValueOrDefault(period);
        var isClosed = row is not null;
        var hasActivity = activityPeriods.Contains(period);

        var canClose = !isClosed && PeriodClosingChainEvaluator.CanCloseMonth(chain, period);
        var canReopen = false;
        DateOnly? blockingPeriod = null;
        string? blockingReason = null;
        string state;

        if (isClosed)
        {
            state = PeriodClosingChainEvaluator.IsClosedOutOfSequence(chain, period)
                ? MonthClosedOutOfSequenceState
                : MonthClosedState;

            if (state == MonthClosedOutOfSequenceState)
            {
                blockingPeriod = chain.NextClosablePeriod;
                blockingReason = BlockingLaterClosedMonths;
            }

            if (chain.LatestClosedPeriod == period)
            {
                var fiscalYearDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{period:yyyy-MM-dd}");
                var fiscalYearRow = (await postingStateReader.GetPageAsync(
                    new PostingStatePageRequest
                    {
                        DocumentId = fiscalYearDocumentId,
                        Operation = PostingOperation.CloseFiscalYear,
                        PageSize = 1
                    },
                    ct))
                    .Records
                    .SingleOrDefault();

                if (fiscalYearRow is null)
                {
                    canReopen = true;
                }
                else if (blockingReason is null)
                {
                    blockingPeriod = period;
                    blockingReason = BlockingFiscalYearClose;
                }
            }
        }
        else if (chain.HasBrokenChain && PeriodClosingChainEvaluator.HasLaterClosedPeriods(chain, period))
        {
            state = MonthBlockedByLaterClosedMonthsState;
            blockingPeriod = chain.LatestClosedPeriod;
            blockingReason = BlockingLaterClosedMonths;
        }
        else if (canClose)
        {
            state = chain.CanCloseAnyMonth || PeriodClosingChainEvaluator.IsBeforeChainStart(chain, period)
                ? MonthOpenState
                : MonthReadyToCloseState;
        }
        else if (chain.NextClosablePeriod is not null && period > chain.NextClosablePeriod.Value)
        {
            state = MonthBlockedByEarlierOpenMonthState;
            blockingPeriod = chain.NextClosablePeriod;
            blockingReason = BlockingEarlierOpenMonth;
        }
        else
        {
            state = MonthOpenState;
        }

        return new PeriodCloseStatusDto(
            Period: period,
            State: state,
            IsClosed: isClosed,
            HasActivity: hasActivity,
            ClosedBy: row?.ClosedBy,
            ClosedAtUtc: row?.ClosedAtUtc,
            CanClose: canClose,
            CanReopen: canReopen,
            BlockingPeriod: blockingPeriod,
            BlockingReason: blockingReason);
    }

    private async Task<PeriodClosingChainSnapshot> LoadChainSnapshotAsync(CancellationToken ct)
    {
        var earliestActivityPeriod = await activityReader.GetEarliestActivityPeriodAsync(ct);
        var latestClosedPeriod = await closedPeriodReader.GetLatestClosedPeriodAsync(ct);

        if (latestClosedPeriod is null)
            return PeriodClosingChainEvaluator.Build(earliestActivityPeriod, latestClosedPeriod, []);

        var chainStartPeriod = earliestActivityPeriod ?? latestClosedPeriod.Value;
        if (chainStartPeriod > latestClosedPeriod.Value)
            return PeriodClosingChainEvaluator.Build(earliestActivityPeriod, latestClosedPeriod, []);

        var closedRows = await closedPeriodReader.GetClosedAsync(chainStartPeriod, latestClosedPeriod.Value, ct);
        return PeriodClosingChainEvaluator.Build(
            earliestActivityPeriod,
            latestClosedPeriod,
            closedRows.Select(x => x.Period).ToArray());
    }
}
