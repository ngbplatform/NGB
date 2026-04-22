using NGB.Core.Dimensions;
using NGB.Persistence.Readers;

namespace NGB.Runtime.Reporting.Internal;

/// <summary>
/// Shared helpers for Accounting reports that:
/// - compute opening balance using latest closed balances + roll-forward turnovers
/// - fall back to inception-to-date pre-range turnovers when closed balances are absent
/// - apply <see cref="DimensionScopeBag"/> filters against canonicalized <see cref="DimensionBag"/> rows.
/// </summary>
internal static class AccountingReportHelpers
{
    public static async Task<decimal> ComputeOpeningBalanceAsync(
        Guid accountId,
        DimensionScopeBag? scopeFilter,
        DateOnly fromInclusive,
        IAccountingBalanceReader balanceReader,
        IAccountingTurnoverReader turnoverReader,
        CancellationToken ct)
    {
        var latestClosedBalances = await balanceReader.GetLatestClosedAsync(fromInclusive, ct: ct);
        if (latestClosedBalances.Count == 0)
        {
            if (!TryGetPreviousPeriod(fromInclusive, out var historyTo))
                return 0m;

            var historicalTurnovers = await turnoverReader.GetRangeAsync(DateOnly.MinValue, historyTo, ct);

            return historicalTurnovers
                .Where(t => t.AccountId == accountId && MatchesScopes(t.Dimensions, scopeFilter))
                .Sum(t => t.DebitAmount - t.CreditAmount);
        }

        var latestClosedPeriod = latestClosedBalances[0].Period;

        var matches = latestClosedBalances
            .Where(b => b.AccountId == accountId && MatchesScopes(b.Dimensions, scopeFilter))
            .ToList();

        var startPeriod = latestClosedPeriod;

        decimal startBalance;
        if (matches.Count == 0)
        {
            startBalance = 0m;
        }
        else
        {
            if (latestClosedPeriod == fromInclusive)
                return matches.Sum(x => x.OpeningBalance);

            startBalance = matches.Sum(x => x.ClosingBalance);
        }

        if (startPeriod >= fromInclusive)
            return startBalance;

        var rollFrom = startPeriod.AddMonths(1);
        var rollTo = fromInclusive.AddMonths(-1);

        if (rollTo < rollFrom)
            return startBalance;

        var turnovers = await turnoverReader.GetRangeAsync(rollFrom, rollTo, ct);

        var delta = turnovers
            .Where(t => t.AccountId == accountId && MatchesScopes(t.Dimensions, scopeFilter))
            .Sum(t => t.DebitAmount - t.CreditAmount);

        return startBalance + delta;
    }

    private static bool TryGetPreviousPeriod(DateOnly period, out DateOnly previous)
    {
        if (period is { Year: 1, Month: 1 })
        {
            previous = default;
            return false;
        }

        previous = period.AddMonths(-1);
        return true;
    }

    public static bool MatchesScopes(DimensionBag row, DimensionScopeBag? scopes)
    {
        if (scopes is null || scopes.IsEmpty)
            return true;

        if (row.IsEmpty)
            return false;

        foreach (var scope in scopes)
        {
            var matched = false;

            foreach (var item in row)
            {
                if (item.DimensionId != scope.DimensionId)
                    continue;

                if (scope.ValueIds.Contains(item.ValueId))
                {
                    matched = true;
                    break;
                }

                return false;
            }

            if (!matched)
                return false;
        }

        return true;
    }

    public static bool MatchesEitherSide(DimensionBag debit, DimensionBag credit, DimensionScopeBag? scopes)
        => MatchesScopes(debit, scopes) || MatchesScopes(credit, scopes);
}
