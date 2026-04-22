using Microsoft.Extensions.Logging;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Reports.CashFlowIndirect;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Exceptions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

public sealed class CashFlowIndirectReportService(
    ICashFlowIndirectSnapshotReader snapshotReader,
    ILogger<CashFlowIndirectReportService> logger)
    : ICashFlowIndirectReportReader
{
    private const string NetIncomeLineCode = "NET_INCOME";
    private const int RollForwardWarningThresholdPeriods = 12;

    public async Task<CashFlowIndirectReport> GetAsync(
        CashFlowIndirectReportRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        request.Validate();

        var snapshot = await snapshotReader.GetAsync(request.FromInclusive, request.ToInclusive, ct);

        WarnOnSlowPath(snapshot, request);
        FailOnUnclassifiedCash(snapshot);

        var operatingLines = new List<CashFlowIndirectLine>
        {
            new()
            {
                LineCode = NetIncomeLineCode,
                Label = "Net income",
                Amount = snapshot.NetIncome,
                IsSynthetic = true
            }
        };
        operatingLines.AddRange(snapshot.OperatingLines
            .Where(x => x.Amount != 0m)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Label, StringComparer.Ordinal)
            .Select(x => new CashFlowIndirectLine
            {
                LineCode = x.LineCode,
                Label = x.Label,
                Amount = x.Amount,
                IsSynthetic = false
            }));

        var investingLines = snapshot.InvestingLines
            .Where(x => x.Amount != 0m)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Label, StringComparer.Ordinal)
            .Select(x => new CashFlowIndirectLine
            {
                LineCode = x.LineCode,
                Label = x.Label,
                Amount = x.Amount,
                IsSynthetic = false
            })
            .ToArray();

        var financingLines = snapshot.FinancingLines
            .Where(x => x.Amount != 0m)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Label, StringComparer.Ordinal)
            .Select(x => new CashFlowIndirectLine
            {
                LineCode = x.LineCode,
                Label = x.Label,
                Amount = x.Amount,
                IsSynthetic = false
            })
            .ToArray();

        var operatingTotal = operatingLines.Sum(x => x.Amount);
        var investingTotal = investingLines.Sum(x => x.Amount);
        var financingTotal = financingLines.Sum(x => x.Amount);
        var netChangeInCash = operatingTotal + investingTotal + financingTotal;

        if (snapshot.EndingCash - snapshot.BeginningCash != netChangeInCash)
        {
            throw AccountingReportValidationException.CashFlowIndirectReconciliationFailed(
                snapshot.BeginningCash,
                snapshot.EndingCash,
                operatingTotal,
                investingTotal,
                financingTotal);
        }

        return new CashFlowIndirectReport
        {
            FromInclusive = request.FromInclusive,
            ToInclusive = request.ToInclusive,
            Sections =
            [
                new CashFlowIndirectSectionModel
                {
                    Section = CashFlowSection.Operating,
                    Label = "Operating Activities",
                    Lines = operatingLines,
                    Total = operatingTotal
                },
                new CashFlowIndirectSectionModel
                {
                    Section = CashFlowSection.Investing,
                    Label = "Investing Activities",
                    Lines = investingLines,
                    Total = investingTotal
                },
                new CashFlowIndirectSectionModel
                {
                    Section = CashFlowSection.Financing,
                    Label = "Financing Activities",
                    Lines = financingLines,
                    Total = financingTotal
                }
            ],
            BeginningCash = snapshot.BeginningCash,
            NetIncreaseDecreaseInCash = netChangeInCash,
            EndingCash = snapshot.EndingCash
        };
    }

    private void WarnOnSlowPath(CashFlowIndirectSnapshot snapshot, CashFlowIndirectReportRequest request)
    {
        if (snapshot.BeginningLatestClosedPeriod is null)
        {
            logger.LogWarning(
                "Cash Flow Statement (Indirect) beginning cash endpoint is using inception-to-date register activity because no closed balances snapshot exists. fromInclusive={FromInclusive}",
                request.FromInclusive);
        }
        else if (snapshot.BeginningRollForwardPeriods > RollForwardWarningThresholdPeriods)
        {
            logger.LogWarning(
                "Cash Flow Statement (Indirect) beginning cash endpoint spans many roll-forward periods. fromInclusive={FromInclusive} beginningLatestClosedPeriod={BeginningLatestClosedPeriod} beginningRollForwardPeriods={BeginningRollForwardPeriods}",
                request.FromInclusive,
                snapshot.BeginningLatestClosedPeriod,
                snapshot.BeginningRollForwardPeriods);
        }

        if (snapshot.EndingLatestClosedPeriod is null)
        {
            logger.LogWarning(
                "Cash Flow Statement (Indirect) ending cash endpoint is using inception-to-date register activity because no closed balances snapshot exists. toInclusive={ToInclusive}",
                request.ToInclusive);
        }
        else if (snapshot.EndingRollForwardPeriods > RollForwardWarningThresholdPeriods)
        {
            logger.LogWarning(
                "Cash Flow Statement (Indirect) ending cash endpoint spans many roll-forward periods. toInclusive={ToInclusive} endingLatestClosedPeriod={EndingLatestClosedPeriod} endingRollForwardPeriods={EndingRollForwardPeriods}",
                request.ToInclusive,
                snapshot.EndingLatestClosedPeriod,
                snapshot.EndingRollForwardPeriods);
        }
    }

    private static void FailOnUnclassifiedCash(CashFlowIndirectSnapshot snapshot)
    {
        var rows = snapshot.UnclassifiedCashRows.Where(x => x.Amount != 0m).ToArray();
        if (rows.Length == 0)
            return;

        var details = string.Join(
            "; ",
            rows.Select(x => $"{x.AccountCode} {x.AccountName}: {x.Amount:0.##}"));

        throw AccountingReportValidationException.CashFlowIndirectUnclassifiedCash(details, rows.Length);
    }
}
