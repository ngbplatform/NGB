using NGB.Accounting.Registers;
using NGB.Tools.Exceptions;

namespace NGB.Accounting.Posting;

public static class AccountingStornoFactory
{
    public static IReadOnlyList<AccountingEntry> Create(
        IReadOnlyList<AccountingEntry> entries,
        DateTime? stornoPeriodUtc = null)
    {
        if (entries is null)
            throw new NgbArgumentRequiredException(nameof(entries));

        if (entries.Count == 0)
            return [];

        return entries.Select(e => new AccountingEntry
        {
            DocumentId = e.DocumentId,
            Period = stornoPeriodUtc ?? e.Period,
            Debit = e.Credit,
            Credit = e.Debit,
            Amount = e.Amount,
            IsStorno = true,
            DebitDimensions = e.CreditDimensions,
            CreditDimensions = e.DebitDimensions,
            DebitDimensionSetId = e.CreditDimensionSetId,
            CreditDimensionSetId = e.DebitDimensionSetId
        }).ToList();
    }
}
