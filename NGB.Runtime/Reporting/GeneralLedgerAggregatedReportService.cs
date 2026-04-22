using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Builds paged General Ledger (aggregated account card):
/// - aggregated detail rows grouped by document + counter-account,
/// - opening balance and full-range totals from a specialized summary reader,
/// - running balance per aggregated row,
/// - cursor-carried totals so continuation pages do not re-read summary state.
///
/// NOTE: this report is DimensionSet-first (canonical dimensions). Fixed-slot projection (first 3 dimensions)
/// is intentionally not supported here.
/// </summary>
public sealed class GeneralLedgerAggregatedReportService(
    IGeneralLedgerAggregatedPageReader pageReader,
    IGeneralLedgerAggregatedSnapshotReader snapshotReader,
    IChartOfAccountsRepository chartOfAccountsRepository)
    : IGeneralLedgerAggregatedPagedReportReader
{
    public async Task<GeneralLedgerAggregatedReportPage> GetPageAsync(
        GeneralLedgerAggregatedReportPageRequest request,
        CancellationToken ct = default)
    {
        ValidateRequest(request);

        var cursor = request.DisablePaging ? null : request.Cursor;
        var dimensionScopes = request.DimensionScopes;
        GeneralLedgerAggregatedSnapshot? snapshot = null;
        if (cursor?.TotalDebit is null
            || cursor.TotalCredit is null
            || cursor.ClosingBalance is null)
        {
            snapshot = await snapshotReader.GetAsync(
                request.AccountId,
                request.FromInclusive,
                request.ToInclusive,
                dimensionScopes,
                ct);
        }

        var opening = cursor?.RunningBalance ?? snapshot!.OpeningBalance;
        var totalDebit = cursor?.TotalDebit ?? snapshot!.TotalDebit;
        var totalCredit = cursor?.TotalCredit ?? snapshot!.TotalCredit;
        var closingBalance = cursor?.ClosingBalance ?? snapshot!.ClosingBalance;
        var snapshotAccountCode = snapshot?.AccountCode;

        var rawPage = await pageReader.GetPageAsync(
            new GeneralLedgerAggregatedPageRequest
            {
                AccountId = request.AccountId,
                FromInclusive = request.FromInclusive,
                ToInclusive = request.ToInclusive,
                DimensionScopes = dimensionScopes,
                PageSize = request.PageSize,
                DisablePaging = request.DisablePaging,
                Cursor = cursor is null
                    ? null
                    : new GeneralLedgerAggregatedLineCursor
                    {
                        AfterPeriodUtc = cursor.AfterPeriodUtc,
                        AfterDocumentId = cursor.AfterDocumentId,
                        AfterCounterAccountCode = cursor.AfterCounterAccountCode,
                        AfterCounterAccountId = cursor.AfterCounterAccountId,
                        AfterDimensionSetId = cursor.AfterDimensionSetId
                    }
            },
            ct);

        var running = opening;
        var reportLines = new List<GeneralLedgerAggregatedReportLine>(rawPage.Lines.Count);
        foreach (var line in rawPage.Lines)
        {
            running += line.Delta;
            reportLines.Add(new GeneralLedgerAggregatedReportLine
            {
                PeriodUtc = line.PeriodUtc,
                DocumentId = line.DocumentId,
                AccountId = line.AccountId,
                AccountCode = line.AccountCode,
                CounterAccountId = line.CounterAccountId,
                CounterAccountCode = line.CounterAccountCode,
                DimensionSetId = line.DimensionSetId,
                Dimensions = line.Dimensions,
                DimensionValueDisplays = line.DimensionValueDisplays,
                DebitAmount = line.DebitAmount,
                CreditAmount = line.CreditAmount,
                RunningBalance = running
            });
        }

        var accountCode = reportLines.Count > 0
            ? reportLines[0].AccountCode
            : !string.IsNullOrWhiteSpace(snapshotAccountCode)
                ? snapshotAccountCode
                : await chartOfAccountsRepository.GetCodeByIdAsync(request.AccountId, ct) ?? request.AccountId.ToString();

        var nextCursor = rawPage.HasMore && reportLines.Count > 0
            ? new GeneralLedgerAggregatedReportCursor
            {
                AfterPeriodUtc = reportLines[^1].PeriodUtc,
                AfterDocumentId = reportLines[^1].DocumentId,
                AfterCounterAccountCode = reportLines[^1].CounterAccountCode,
                AfterCounterAccountId = reportLines[^1].CounterAccountId,
                AfterDimensionSetId = reportLines[^1].DimensionSetId,
                RunningBalance = reportLines[^1].RunningBalance,
                TotalDebit = totalDebit,
                TotalCredit = totalCredit,
                ClosingBalance = closingBalance
            }
            : null;

        return new GeneralLedgerAggregatedReportPage
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
            HasMore = rawPage.HasMore,
            NextCursor = nextCursor
        };
    }

    private static void ValidateRequest(GeneralLedgerAggregatedReportPageRequest request)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (request.AccountId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(request.AccountId));

        if (request.ToInclusive < request.FromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(request.ToInclusive), request.ToInclusive, "To must be on or after From.");

        request.FromInclusive.EnsureMonthStart(nameof(request.FromInclusive));
        request.ToInclusive.EnsureMonthStart(nameof(request.ToInclusive));

        if (request is { DisablePaging: false, PageSize: <= 0 })
            throw new NgbArgumentOutOfRangeException(nameof(request.PageSize), request.PageSize, "Page size must be greater than 0.");
    }
}
