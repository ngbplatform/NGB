using NGB.Accounting.Reports.AccountCard;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Canonical Account Card effective paging service.
/// Builds report pages over a deterministic effective stream so that the UI can use true cursor paging
/// with stable running balances and cursor-carried report totals.
/// </summary>
public sealed class AccountCardEffectivePagedReportService(
    IAccountCardEffectivePageReader pageReader,
    IAccountingBalanceReader balanceReader,
    IAccountingTurnoverReader turnoverReader,
    IChartOfAccountsRepository chartOfAccountsRepository)
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

        var rangeOpening = cursor is null
            ? await AccountingReportHelpers.ComputeOpeningBalanceAsync(
                request.AccountId,
                dimensionScopes,
                request.FromInclusive,
                balanceReader,
                turnoverReader,
                ct)
            : cursor.RunningBalance;

        var needTotals = cursor?.TotalDebit is null
                         || cursor.TotalCredit is null
                         || cursor.ClosingBalance is null;

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
                    AfterEntryId = cursor.AfterEntryId
                },
            PageSize = request.PageSize,
            DisablePaging = request.DisablePaging,
            IncludeTotals = needTotals
        }, ct);

        var opening = cursor?.RunningBalance ?? rangeOpening;
        var totalDebit = cursor?.TotalDebit ?? effectivePage.TotalDebit
            ?? throw new NgbInvariantViolationException("Account Card effective reader must provide total debit when totals are requested.");
        var totalCredit = cursor?.TotalCredit ?? effectivePage.TotalCredit
            ?? throw new NgbInvariantViolationException("Account Card effective reader must provide total credit when totals are requested.");
        var closingBalance = cursor?.ClosingBalance ?? (rangeOpening + (totalDebit - totalCredit));

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
