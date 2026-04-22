using NGB.Accounting.Reports.AccountingConsistency;
using NGB.Persistence.Checkers;
using NGB.Persistence.Readers.Reports;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Reporting;

public sealed class AccountingConsistencyReportService(
    IAccountingIntegrityDiagnostics integrityDiagnostics,
    IAccountingConsistencySnapshotReader snapshotReader)
    : IAccountingConsistencyReportReader
{
    public async Task<AccountingConsistencyReport> RunForPeriodAsync(
        DateOnly period,
        DateOnly? previousPeriodForChainCheck = null,
        CancellationToken ct = default)
    {
        period.EnsureMonthStart(nameof(period));
        previousPeriodForChainCheck?.EnsureMonthStart(nameof(previousPeriodForChainCheck));

        var issues = new List<AccountingConsistencyIssue>();

        // 1) Turnovers vs Register (DB-level diagnostic)
        var diffCount = await integrityDiagnostics.GetTurnoversVsRegisterDiffCountAsync(period, ct);
        if (diffCount != 0)
        {
            issues.Add(new AccountingConsistencyIssue
            {
                Kind = AccountingConsistencyIssueKind.TurnoversVsRegisterMismatch,
                Period = period,
                Message = $"Stored turnovers differ from register aggregation for period {period:yyyy-MM-dd}. Diff rows: {diffCount}."
            });
        }

        // 2) Consistency snapshot (balances + turnovers + optional previous chain rows).
        var snapshot = await snapshotReader.GetAsync(period, previousPeriodForChainCheck, ct);
        var rows = snapshot.Rows;
        var hasCurrentBalanceSnapshot = rows.Any(x => x.HasCurrentBalanceRow);

        var balanceVsTurnoverMismatchCount = 0L;

        foreach (var row in rows.Where(x => x.HasCurrentBalanceRow))
        {
            var expectedClosing = row.OpeningBalance + (row.DebitAmount - row.CreditAmount);
            if (expectedClosing == row.ClosingBalance)
                continue;

            balanceVsTurnoverMismatchCount++;

            issues.Add(new AccountingConsistencyIssue
            {
                Kind = AccountingConsistencyIssueKind.BalanceVsTurnoverMismatch,
                Period = period,
                AccountId = row.AccountId,
                AccountCode = row.AccountCode,
                DimensionSetId = row.DimensionSetId,
                Message = $"Balance mismatch. Expected closing={expectedClosing}, actual closing={row.ClosingBalance}, opening={row.OpeningBalance}, debit={row.DebitAmount}, credit={row.CreditAmount}."
            });
        }

        // 2.1) Detect turnover keys that exist without balances.
        // IMPORTANT: balances are snapshotted data produced by closing.
        // For an OPEN (not-closed) period balances may legitimately be absent, so MissingKey check is performed only
        // when the period has a balance snapshot.
        var missingKeyCount = 0L;

        if (hasCurrentBalanceSnapshot)
        {
            foreach (var row in rows.Where(x => x.HasTurnoverRow && !x.HasCurrentBalanceRow))
            {
                missingKeyCount++;

                issues.Add(new AccountingConsistencyIssue
                {
                    Kind = AccountingConsistencyIssueKind.MissingKey,
                    Period = period,
                    AccountId = row.AccountId,
                    AccountCode = row.AccountCode,
                    DimensionSetId = row.DimensionSetId,
                    Message = "Turnover exists but balance row is missing for this key."
                });
            }
        }

        // 3) Optional chain check between periods (previous closing -> current opening)
        var balanceChainMismatchCount = 0L;

        if (previousPeriodForChainCheck is not null && hasCurrentBalanceSnapshot)
        {
            foreach (var row in rows)
            {
                var prevClosing = row.HasPreviousBalanceRow ? row.PreviousClosingBalance : 0m;
                var currentOpening = row.HasCurrentBalanceRow ? row.OpeningBalance : 0m;

                if (prevClosing == currentOpening)
                    continue;

                balanceChainMismatchCount++;

                issues.Add(new AccountingConsistencyIssue
                {
                    Kind = AccountingConsistencyIssueKind.ClosedPeriodChainBroken, // alias of BalanceChainMismatch
                    Period = period,
                    PreviousPeriod = previousPeriodForChainCheck,
                    AccountId = row.AccountId,
                    AccountCode = row.AccountCode,
                    DimensionSetId = row.DimensionSetId,
                    Message = $"Closed chain mismatch. Previous closing={prevClosing}, current opening={currentOpening}."
                });
            }
        }

        return new AccountingConsistencyReport
        {
            Period = period,
            PreviousPeriodForChainCheck = previousPeriodForChainCheck,
            TurnoversVsRegisterDiffCount = diffCount,
            BalanceVsTurnoverMismatchCount = balanceVsTurnoverMismatchCount,
            BalanceChainMismatchCount = balanceChainMismatchCount,
            MissingKeyCount = missingKeyCount,
            Issues = issues
        };
    }
}
