using NGB.Accounting.Registers;

namespace NGB.Runtime.Documents.Posting;

internal static class StoredAccountingEffect
{
    public static IReadOnlyList<AccountingEntry> GetEntriesToReverse(IReadOnlyList<AccountingEntry> history)
    {
        if (history.Count == 0)
            return history;

        var entries = history.OrderBy(entry => entry.EntryId).ToArray();
        var unmatched = new Dictionary<EntryKey, Stack<int>>();
        var cancelled = new bool[entries.Length];
        (int Original, int Storno)? lastPair = null;

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var reverseKey = Key(entry, reverse: true);
            if (entry.IsStorno && unmatched.TryGetValue(reverseKey, out var originals) && originals.Count > 0)
            {
                var original = originals.Pop();
                cancelled[original] = true;
                cancelled[index] = true;
                lastPair = (original, index);
                continue;
            }

            var key = Key(entry, reverse: false);
            if (!unmatched.TryGetValue(key, out var candidates))
            {
                candidates = new Stack<int>();
                unmatched.Add(key, candidates);
            }

            candidates.Push(index);
        }

        if (lastPair is null)
            return history;

        var remaining = entries.Where((_, index) => !cancelled[index]).ToArray();
        if (remaining.Length > 0)
            return remaining;

        // A posting may legitimately contain a complete zero-net reversal pair.
        // Retain the last such pair so its reversal still records a nonempty accounting
        // operation, without replaying all earlier zero-net cycles.
        return [entries[lastPair.Value.Original], entries[lastPair.Value.Storno]];
    }

    private static EntryKey Key(AccountingEntry entry, bool reverse)
        => new(
            entry.DocumentId,
            entry.Period,
            reverse ? entry.Credit.Id : entry.Debit.Id,
            reverse ? entry.Debit.Id : entry.Credit.Id,
            reverse ? entry.CreditDimensionSetId : entry.DebitDimensionSetId,
            reverse ? entry.DebitDimensionSetId : entry.CreditDimensionSetId,
            entry.Amount);

    private readonly record struct EntryKey(
        Guid DocumentId,
        DateTime Period,
        Guid DebitAccountId,
        Guid CreditAccountId,
        Guid DebitDimensionSetId,
        Guid CreditDimensionSetId,
        decimal Amount);
}
