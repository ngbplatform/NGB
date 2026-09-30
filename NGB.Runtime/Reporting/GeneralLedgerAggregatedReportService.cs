using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Persistence.Accounts;
using NGB.Persistence.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Builds paged General Ledger (aggregated account card):
/// - aggregated detail rows grouped by document + counter-account,
/// - opening balance and full-range totals from a specialized summary reader,
/// - running balance per aggregated row,
/// - totals reused only within the same read session, with fresh prefix balances for live pages.
///
/// NOTE: this report is DimensionSet-first (canonical dimensions). Fixed-slot projection (first 3 dimensions)
/// is intentionally not supported here.
/// </summary>
public sealed class GeneralLedgerAggregatedReportService(
    IGeneralLedgerAggregatedPageReader pageReader,
    IGeneralLedgerAggregatedSnapshotReader snapshotReader,
    IChartOfAccountsRepository chartOfAccountsRepository,
    IReportReadSession? session = null)
    : IGeneralLedgerAggregatedPagedReportReader
{
    public async Task<GeneralLedgerAggregatedReportPage> GetPageAsync(
        GeneralLedgerAggregatedReportPageRequest request,
        CancellationToken ct = default)
    {
        ValidateRequest(request);

        var cursor = request.DisablePaging ? null : request.Cursor;
        var dimensionScopes = request.DimensionScopes;
        var snapshotId = session?.SnapshotId ?? Guid.Empty;
        
        var reuseBalances = cursor is not null
            && snapshotId != Guid.Empty 
            && cursor.SnapshotId == snapshotId
            && cursor is { TotalDebit: not null, TotalCredit: not null, ClosingBalance: not null };

        var snapshot = reuseBalances
            ? null
            : await snapshotReader.GetAsync(request.AccountId, request.FromInclusive, request.ToInclusive, dimensionScopes, ct);

        var totalDebit = reuseBalances ? cursor!.TotalDebit!.Value : snapshot!.TotalDebit;
        var totalCredit = reuseBalances ? cursor!.TotalCredit!.Value : snapshot!.TotalCredit;
        var closingBalance = reuseBalances ? cursor!.ClosingBalance!.Value : snapshot!.ClosingBalance;
        var snapshotAccountCode = snapshot?.AccountCode;

        var rawPage = await pageReader.GetPageAsync(
            new GeneralLedgerAggregatedPageRequest
            {
                AccountId = request.AccountId,
                FromInclusive = request.FromInclusive,
                ToInclusive = request.ToInclusive,
                DimensionScopes = dimensionScopes,
                IncludePrefixDelta = cursor is not null && !reuseBalances,
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

        var opening = reuseBalances ? cursor!.RunningBalance : snapshot!.OpeningBalance + rawPage.PrefixDelta;
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
                SnapshotId = snapshotId,
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
    }
}
