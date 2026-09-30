using NGB.Accounting.Reports.TrialBalance;
using NGB.Persistence.Readers.Reports;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

/// <summary>Compatibility reader for callers requiring a full summary. Interactive and export
/// endpoints use the durable streaming execution path.</summary>
public sealed class TrialBalanceReportService(ITrialBalanceAccountSummaryReader reader) : ITrialBalanceReportReader
{
    public async Task<TrialBalanceReportPage> GetPageAsync(
        TrialBalanceReportPageRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var rows = new List<TrialBalanceReportRow>();
        var totals = new TrialBalanceReportTotals(0, 0, 0, 0);

        await foreach (var row in TrialBalanceRows.ReadAsync(
            reader.ReadAsync(request.FromInclusive, request.ToInclusive, request.DimensionScopes, ct),
            request.ShowSubtotals,
            ct))
        {
            rows.Add(row);
            if (row.RowKind == TrialBalanceReportRowKind.Detail)
            {
                
                totals = new(
                    totals.OpeningBalance + row.OpeningBalance,
                    totals.DebitAmount + row.DebitAmount,
                    totals.CreditAmount + row.CreditAmount,
                    totals.ClosingBalance + row.ClosingBalance);
            }
        }
        return new(rows, rows.Count, false, totals);
    }
}
