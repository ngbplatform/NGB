namespace NGB.Runtime.Periods;

internal sealed record PeriodClosingChainSnapshot(
    DateOnly? EarliestActivityPeriod,
    DateOnly? ChainStartPeriod,
    DateOnly? LatestClosedPeriod,
    DateOnly? LatestContiguousClosedPeriod,
    DateOnly? NextClosablePeriod,
    bool CanCloseAnyMonth,
    bool HasBrokenChain,
    DateOnly? FirstGapPeriod);

internal static class PeriodClosingChainEvaluator
{
    public static PeriodClosingChainSnapshot Build(
        DateOnly? earliestActivityPeriod,
        DateOnly? latestClosedPeriod,
        IReadOnlyCollection<DateOnly> closedPeriodsInChainRange)
    {
        if (earliestActivityPeriod is null && latestClosedPeriod is null)
        {
            return new PeriodClosingChainSnapshot(
                EarliestActivityPeriod: null,
                ChainStartPeriod: null,
                LatestClosedPeriod: null,
                LatestContiguousClosedPeriod: null,
                NextClosablePeriod: null,
                CanCloseAnyMonth: true,
                HasBrokenChain: false,
                FirstGapPeriod: null);
        }

        var chainStartPeriod = earliestActivityPeriod ?? latestClosedPeriod;
        if (chainStartPeriod is null)
        {
            return new PeriodClosingChainSnapshot(
                EarliestActivityPeriod: earliestActivityPeriod,
                ChainStartPeriod: null,
                LatestClosedPeriod: latestClosedPeriod,
                LatestContiguousClosedPeriod: latestClosedPeriod,
                NextClosablePeriod: latestClosedPeriod,
                CanCloseAnyMonth: false,
                HasBrokenChain: false,
                FirstGapPeriod: null);
        }

        if (latestClosedPeriod is null || chainStartPeriod > latestClosedPeriod)
        {
            return new PeriodClosingChainSnapshot(
                EarliestActivityPeriod: earliestActivityPeriod,
                ChainStartPeriod: chainStartPeriod,
                LatestClosedPeriod: latestClosedPeriod,
                LatestContiguousClosedPeriod: null,
                NextClosablePeriod: chainStartPeriod,
                CanCloseAnyMonth: false,
                HasBrokenChain: false,
                FirstGapPeriod: null);
        }

        var closedSet = closedPeriodsInChainRange.ToHashSet();
        var cursor = chainStartPeriod.Value;

        while (cursor <= latestClosedPeriod && closedSet.Contains(cursor))
        {
            cursor = cursor.AddMonths(1);
        }

        DateOnly? firstGapPeriod = cursor <= latestClosedPeriod ? cursor : null;
        var latestContiguousClosedPeriod = firstGapPeriod?.AddMonths(-1) ?? latestClosedPeriod;

        return new PeriodClosingChainSnapshot(
            EarliestActivityPeriod: earliestActivityPeriod,
            ChainStartPeriod: chainStartPeriod,
            LatestClosedPeriod: latestClosedPeriod,
            LatestContiguousClosedPeriod: latestContiguousClosedPeriod,
            NextClosablePeriod: firstGapPeriod ?? latestClosedPeriod.Value.AddMonths(1),
            CanCloseAnyMonth: false,
            HasBrokenChain: firstGapPeriod is not null,
            FirstGapPeriod: firstGapPeriod);
    }

    public static bool IsBeforeChainStart(PeriodClosingChainSnapshot snapshot, DateOnly period)
        => snapshot.ChainStartPeriod is not null && period < snapshot.ChainStartPeriod.Value;

    public static bool HasLaterClosedPeriods(PeriodClosingChainSnapshot snapshot, DateOnly period)
    {
        if (snapshot.LatestClosedPeriod is null || snapshot.LatestClosedPeriod <= period)
            return false;

        return snapshot.ChainStartPeriod is not null && period >= snapshot.ChainStartPeriod.Value;
    }

    public static bool CanCloseMonth(PeriodClosingChainSnapshot snapshot, DateOnly period)
    {
        if (snapshot.CanCloseAnyMonth)
            return true;

        if (IsBeforeChainStart(snapshot, period))
            return true;

        if (snapshot.HasBrokenChain && HasLaterClosedPeriods(snapshot, period))
            return false;

        return snapshot.NextClosablePeriod is not null && snapshot.NextClosablePeriod.Value == period;
    }

    public static bool IsClosedOutOfSequence(PeriodClosingChainSnapshot snapshot, DateOnly period)
        => snapshot is { HasBrokenChain: true, NextClosablePeriod: not null }
           && period > snapshot.NextClosablePeriod.Value;
}
