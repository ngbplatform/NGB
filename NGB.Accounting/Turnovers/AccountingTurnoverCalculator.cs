using NGB.Accounting.Periods;
using NGB.Accounting.Registers;

namespace NGB.Accounting.Turnovers;

public sealed class AccountingTurnoverCalculator
{
    /// <summary>
    /// Calculates monthly turnovers from register entries.
    ///
    /// IMPORTANT:
    /// - all entries must belong to the same month (period_month)
    /// - grouping key is (AccountId, DimensionSetId)
    ///
    /// This matches the SQL CHECK constraint on accounting_turnovers.period.
    /// </summary>
    public IReadOnlyList<AccountingTurnover> Calculate(IReadOnlyList<AccountingEntry> entries)
    {
        if (entries.Count == 0)
            return [];

        var period = AccountingPeriod.FromDateTime(entries[0].Period);

        // Debit side
        var debit = entries
            .GroupBy(e => (e.Debit.Id, e.DebitDimensionSetId))
            .Select(g =>
            {
                var sample = g.First();

                return new AccountingTurnover
                {
                    Period = period,
                    AccountId = sample.Debit.Id,
                    DimensionSetId = sample.DebitDimensionSetId,
                    Dimensions = sample.DebitDimensions,
                    AccountCode = sample.Debit.Code,
                    DebitAmount = g.Sum(x => x.Amount),
                    CreditAmount = 0m
                };
            })
            .ToList();

        // Credit side
        var credit = entries
            .GroupBy(e => (e.Credit.Id, e.CreditDimensionSetId))
            .Select(g =>
            {
                var sample = g.First();

                return new AccountingTurnover
                {
                    Period = period,
                    AccountId = sample.Credit.Id,
                    DimensionSetId = sample.CreditDimensionSetId,
                    Dimensions = sample.CreditDimensions,
                    AccountCode = sample.Credit.Code,
                    DebitAmount = 0m,
                    CreditAmount = g.Sum(x => x.Amount)
                };
            })
            .ToList();

        // Merge both sides by (AccountId, DimensionSetId)
        return debit
            .Concat(credit)
            .GroupBy(x => (x.AccountId, x.DimensionSetId))
            .Select(g =>
            {
                var sample = g.First();
                return new AccountingTurnover
                {
                    Period = period,
                    AccountId = sample.AccountId,
                    DimensionSetId = sample.DimensionSetId,
                    Dimensions = sample.Dimensions,
                    AccountCode = sample.AccountCode,
                    DebitAmount = g.Sum(x => x.DebitAmount),
                    CreditAmount = g.Sum(x => x.CreditAmount)
                };
            })
            .ToList();
    }
}
