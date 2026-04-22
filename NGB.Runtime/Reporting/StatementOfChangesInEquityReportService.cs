using Microsoft.Extensions.Logging;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.StatementOfChangesInEquity;
using NGB.Persistence.Readers.Reports;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Builds a bounded Statement of Changes in Equity as a component rollforward:
/// Opening -> Change -> Closing for real Equity accounts plus a synthetic
/// "Current Earnings (Unclosed)" component derived from P&amp;L balances.
///
/// Important semantics:
/// - the statement is based on two "as of" endpoints (month-end before From, and month-end at To);
/// - before fiscal year close, accumulated unclosed P&amp;L appears in the synthetic current-earnings component;
/// - after fiscal year close, retained earnings reflects the real closing entries and the synthetic component naturally drops to zero.
/// </summary>
public sealed class StatementOfChangesInEquityReportService(
    IStatementOfChangesInEquitySnapshotReader snapshotReader,
    ILogger<StatementOfChangesInEquityReportService> logger)
    : IStatementOfChangesInEquityReportReader
{
    private const string CurrentEarningsCode = "CURR_EARNINGS";
    private const string CurrentEarningsName = "Current Earnings (Unclosed)";
    private const int RollForwardWarningThresholdPeriods = 12;

    public async Task<StatementOfChangesInEquityReport> GetAsync(
        StatementOfChangesInEquityReportRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        request.Validate();

        var snapshot = await snapshotReader.GetAsync(request.FromInclusive, request.ToInclusive, ct);

        if (snapshot.OpeningLatestClosedPeriod is null)
        {
            logger.LogWarning(
                "Statement of Changes in Equity opening endpoint is using inception-to-date turnovers because no closed balances snapshot exists. fromInclusive={FromInclusive}",
                request.FromInclusive);
        }
        else if (snapshot.OpeningRollForwardPeriods > RollForwardWarningThresholdPeriods)
        {
            logger.LogWarning(
                "Statement of Changes in Equity opening endpoint spans many roll-forward periods. fromInclusive={FromInclusive} openingLatestClosedPeriod={OpeningLatestClosedPeriod} openingRollForwardPeriods={OpeningRollForwardPeriods}",
                request.FromInclusive,
                snapshot.OpeningLatestClosedPeriod,
                snapshot.OpeningRollForwardPeriods);
        }

        if (snapshot.ClosingLatestClosedPeriod is null)
        {
            logger.LogWarning(
                "Statement of Changes in Equity closing endpoint is using inception-to-date turnovers because no closed balances snapshot exists. toInclusive={ToInclusive}",
                request.ToInclusive);
        }
        else if (snapshot.ClosingRollForwardPeriods > RollForwardWarningThresholdPeriods)
        {
            logger.LogWarning(
                "Statement of Changes in Equity closing endpoint spans many roll-forward periods. toInclusive={ToInclusive} closingLatestClosedPeriod={ClosingLatestClosedPeriod} closingRollForwardPeriods={ClosingRollForwardPeriods}",
                request.ToInclusive,
                snapshot.ClosingLatestClosedPeriod,
                snapshot.ClosingRollForwardPeriods);
        }

        var lines = new List<StatementOfChangesInEquityLine>();
        var openingIncome = 0m;
        var closingIncome = 0m;
        var openingExpenses = 0m;
        var closingExpenses = 0m;

        foreach (var row in snapshot.Rows)
        {
            switch (row.StatementSection)
            {
                case StatementSection.Equity:
                {
                    var opening = ToPresentedAmount(row.StatementSection, row.OpeningBalance);
                    var closing = ToPresentedAmount(row.StatementSection, row.ClosingBalance);
                    if (opening == 0m && closing == 0m)
                        continue;

                    lines.Add(new StatementOfChangesInEquityLine
                    {
                        AccountId = row.AccountId,
                        ComponentCode = row.AccountCode,
                        ComponentName = row.AccountName,
                        IsSynthetic = false,
                        OpeningAmount = opening,
                        ChangeAmount = closing - opening,
                        ClosingAmount = closing
                    });
                    break;
                }

                case StatementSection.Income:
                case StatementSection.OtherIncome:
                    openingIncome += ToPresentedAmount(row.StatementSection, row.OpeningBalance);
                    closingIncome += ToPresentedAmount(row.StatementSection, row.ClosingBalance);
                    break;

                case StatementSection.CostOfGoodsSold:
                case StatementSection.Expenses:
                case StatementSection.OtherExpense:
                    openingExpenses += ToPresentedAmount(row.StatementSection, row.OpeningBalance);
                    closingExpenses += ToPresentedAmount(row.StatementSection, row.ClosingBalance);
                    break;

                default:
                    throw new NgbInvariantViolationException(
                        $"Statement of Changes in Equity snapshot must contain only Equity and P&L sections. Actual={row.StatementSection} account={row.AccountCode}");
            }
        }

        lines.Sort(static (a, b) =>
        {
            var c = string.CompareOrdinal(a.ComponentCode, b.ComponentCode);
            return c != 0 ? c : string.CompareOrdinal(a.ComponentName, b.ComponentName);
        });

        var openingCurrentEarnings = openingIncome - openingExpenses;
        var closingCurrentEarnings = closingIncome - closingExpenses;
        if (openingCurrentEarnings != 0m || closingCurrentEarnings != 0m)
        {
            lines.Add(new StatementOfChangesInEquityLine
            {
                AccountId = Guid.Empty,
                ComponentCode = CurrentEarningsCode,
                ComponentName = CurrentEarningsName,
                IsSynthetic = true,
                OpeningAmount = openingCurrentEarnings,
                ChangeAmount = closingCurrentEarnings - openingCurrentEarnings,
                ClosingAmount = closingCurrentEarnings
            });
        }

        var totalOpening = lines.Sum(x => x.OpeningAmount);
        var totalClosing = lines.Sum(x => x.ClosingAmount);
        var totalChange = lines.Sum(x => x.ChangeAmount);

        return new StatementOfChangesInEquityReport
        {
            FromInclusive = request.FromInclusive,
            ToInclusive = request.ToInclusive,
            Lines = lines,
            TotalOpening = totalOpening,
            TotalChange = totalChange,
            TotalClosing = totalClosing
        };
    }

    private static decimal ToPresentedAmount(StatementSection section, decimal signedBalance)
    {
        var normalBalance = NormalBalanceDefaults.FromStatementSection(section);
        return normalBalance == NormalBalance.Debit
            ? signedBalance
            : -signedBalance;
    }
}
