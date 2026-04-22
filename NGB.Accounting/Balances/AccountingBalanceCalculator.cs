using NGB.Accounting.Periods;
using NGB.Accounting.Turnovers;
using NGB.Core.Dimensions;

namespace NGB.Accounting.Balances;

public sealed class AccountingBalanceCalculator
{
    /// <summary>
    /// Calculates balance snapshot for the period:
    ///   closing = opening + (debit - credit)
    ///
    /// Keyed by (AccountId, DimensionSetId).
    /// </summary>
    public IEnumerable<AccountingBalance> Calculate(
        IEnumerable<AccountingTurnover> turnovers,
        IEnumerable<AccountingBalance> previousPeriodBalances,
        DateOnly period)
    {
        period = AccountingPeriod.FromDateOnly(period);

        var prevByKey = previousPeriodBalances.ToDictionary(
            b => new AccountingBalanceKey(b.AccountId, b.DimensionSetId),
            b => b);

        var tByKey = turnovers.ToDictionary(
            t => new AccountingBalanceKey(t.AccountId, t.DimensionSetId),
            t => t);

        var allKeys = prevByKey.Keys
            .Concat(tByKey.Keys)
            .Distinct()
            .ToList();

        foreach (var key in allKeys)
        {
            prevByKey.TryGetValue(key, out var prev);
            tByKey.TryGetValue(key, out var t);

            var opening = prev?.ClosingBalance ?? 0m;
            var debit = t?.DebitAmount ?? 0m;
            var credit = t?.CreditAmount ?? 0m;

            yield return new AccountingBalance
            {
                Period = period,
                AccountId = key.AccountId,
                DimensionSetId = key.DimensionSetId,
                Dimensions = prev?.Dimensions ?? t?.Dimensions ?? DimensionBag.Empty,
                AccountCode = prev?.AccountCode ?? t?.AccountCode,
                OpeningBalance = opening,
                ClosingBalance = opening + (debit - credit)
            };
        }
    }
}
