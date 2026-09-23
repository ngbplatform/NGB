using NGB.Accounting.Reports.AccountCard;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Canonical Account Card effective paging service.
/// Builds report pages over a deterministic effective stream so that the UI can use true cursor paging
/// with balances recomputed for each read session; an export reuses totals inside its single snapshot.
/// </summary>
public sealed class AccountCardEffectivePagedReportService(
    IAccountCardEffectivePageReader pageReader,
    IChartOfAccountsRepository chartOfAccountsRepository,
    IReportReadSession? session = null)
    : IAccountCardEffectivePagedReportReader
{
    public async Task<AccountCardReportPage> GetPageAsync(
        AccountCardReportPageRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (request.AccountId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(request.AccountId));

        if (request.ToInclusive < request.FromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(request.ToInclusive), request.ToInclusive, "To must be on or after From.");

        request.FromInclusive.EnsureMonthStart(nameof(request.FromInclusive));
        request.ToInclusive.EnsureMonthStart(nameof(request.ToInclusive));

        var cursor = request.DisablePaging ? null : request.Cursor;
        var dimensionScopes = request.DimensionScopes;
        var snapshotId = session?.SnapshotId ?? Guid.Empty;

        var reuseBalances = cursor is not null
            && snapshotId != Guid.Empty && cursor.SnapshotId == snapshotId
            && cursor is { TotalDebit: not null, TotalCredit: not null, ClosingBalance: not null };

        var rangeOpening = reuseBalances
            ? 0m
            : await pageReader.GetOpeningBalanceAsync(request.AccountId, request.FromInclusive, dimensionScopes, ct);

        var effectivePage = await pageReader.GetPageAsync(new AccountCardLinePageRequest
        {
            AccountId = request.AccountId,
            FromInclusive = request.FromInclusive,
            ToInclusive = request.ToInclusive,
            DimensionScopes = dimensionScopes,
            Cursor = cursor is null
                ? null
                : new AccountCardLineCursor
                {
                    AfterPeriodUtc = cursor.AfterPeriodUtc,
                    AfterEntryId = cursor.AfterEntryId,
                },
            PageSize = request.PageSize,
            DisablePaging = request.DisablePaging,
            IncludeTotals = request.IncludeRangeTotals && !reuseBalances,
            IncludePrefixDelta = cursor is not null && !reuseBalances
        }, ct);

        var opening = reuseBalances ? cursor!.RunningBalance : rangeOpening + effectivePage.PrefixDelta;

        decimal? totalDebit = request.IncludeRangeTotals
            ? reuseBalances
                ? cursor!.TotalDebit!.Value
                : effectivePage.TotalDebit
                    ?? throw new NgbInvariantViolationException("Account Card effective reader must provide total debit when totals are requested.")
            : null;

        decimal? totalCredit = request.IncludeRangeTotals
            ? reuseBalances
                ? cursor!.TotalCredit!.Value
                : effectivePage.TotalCredit
                    ?? throw new NgbInvariantViolationException("Account Card effective reader must provide total credit when totals are requested.")
            : null;

        decimal? closingBalance = reuseBalances
            ? cursor!.ClosingBalance!.Value
            : request.IncludeRangeTotals
                ? rangeOpening + totalDebit!.Value - totalCredit!.Value
                : null;

        var running = opening;
        var reportLines = new List<AccountCardReportLine>(effectivePage.Lines.Count);

        foreach (var l in effectivePage.Lines)
        {
            running += l.Delta;

            reportLines.Add(new AccountCardReportLine
            {
                EntryId = l.EntryId,
                PeriodUtc = l.PeriodUtc,
                DocumentId = l.DocumentId,
                AccountId = l.AccountId,
                AccountCode = l.AccountCode,
                CounterAccountId = l.CounterAccountId,
                CounterAccountCode = l.CounterAccountCode,
                DimensionSetId = l.DimensionSetId,
                Dimensions = l.Dimensions,
                DimensionValueDisplays = l.DimensionValueDisplays,
                DebitAmount = l.DebitAmount,
                CreditAmount = l.CreditAmount,
                Delta = l.Delta,
                RunningBalance = running
            });
        }

        var accountCode = reportLines.Count > 0
            ? reportLines[0].AccountCode
            : await chartOfAccountsRepository.GetCodeByIdAsync(request.AccountId, ct) ?? request.AccountId.ToString();

        var nextCursor = effectivePage.HasMore && reportLines.Count > 0
            ? new AccountCardReportCursor
            {
                AfterPeriodUtc = reportLines[^1].PeriodUtc,
                AfterEntryId = reportLines[^1].EntryId,
                RunningBalance = reportLines[^1].RunningBalance,
                SnapshotId = snapshotId,
                TotalDebit = totalDebit,
                TotalCredit = totalCredit,
                ClosingBalance = closingBalance
            }
            : null;

        return new AccountCardReportPage
        {
            AccountId = request.AccountId,
            AccountCode = accountCode,
            FromInclusive = request.FromInclusive,
            ToInclusive = request.ToInclusive,
            OpeningBalance = opening,
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            ClosingBalance = closingBalance,
            Lines = reportLines,
            HasMore = effectivePage.HasMore,
            NextCursor = nextCursor
        };
    }
}
