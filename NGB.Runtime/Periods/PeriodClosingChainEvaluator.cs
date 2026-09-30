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

        if (latestClosedPeriod is null)
        {
            var firstActivityPeriod = earliestActivityPeriod!.Value;
            return new PeriodClosingChainSnapshot(
                EarliestActivityPeriod: earliestActivityPeriod,
                ChainStartPeriod: firstActivityPeriod,
                LatestClosedPeriod: latestClosedPeriod,
                LatestContiguousClosedPeriod: null,
                NextClosablePeriod: firstActivityPeriod,
                CanCloseAnyMonth: false,
                HasBrokenChain: false,
                FirstGapPeriod: null);
        }

        var latest = latestClosedPeriod.Value;
        var chainStartPeriod = earliestActivityPeriod.GetValueOrDefault(latest);
        if (chainStartPeriod > latest)
        {
            return new PeriodClosingChainSnapshot(
                EarliestActivityPeriod: earliestActivityPeriod,
                ChainStartPeriod: chainStartPeriod,
                LatestClosedPeriod: latest,
                LatestContiguousClosedPeriod: null,
                NextClosablePeriod: chainStartPeriod,
                CanCloseAnyMonth: false,
                HasBrokenChain: false,
                FirstGapPeriod: null);
        }

        var closedSet = closedPeriodsInChainRange.ToHashSet();
        var cursor = chainStartPeriod;

        while (cursor <= latest && closedSet.Contains(cursor))
        {
            cursor = cursor.AddMonths(1);
        }

        DateOnly? firstGapPeriod = cursor <= latest ? cursor : null;
        var latestContiguousClosedPeriod = firstGapPeriod?.AddMonths(-1) ?? latest;

        return new PeriodClosingChainSnapshot(
            EarliestActivityPeriod: earliestActivityPeriod,
            ChainStartPeriod: chainStartPeriod,
            LatestClosedPeriod: latest,
            LatestContiguousClosedPeriod: latestContiguousClosedPeriod,
            NextClosablePeriod: firstGapPeriod ?? latest.AddMonths(1),
            CanCloseAnyMonth: false,
            HasBrokenChain: firstGapPeriod is not null,
            FirstGapPeriod: firstGapPeriod);
    }

    public static bool IsBeforeChainStart(PeriodClosingChainSnapshot snapshot, DateOnly period)
        => snapshot.ChainStartPeriod is not null && period < snapshot.ChainStartPeriod.Value;

    public static bool HasLaterClosedPeriods(PeriodClosingChainSnapshot snapshot, DateOnly period)
    {
        if (snapshot.LatestClosedPeriod is null)
            return false;

        if (snapshot.LatestClosedPeriod.Value <= period)
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

        return snapshot.NextClosablePeriod == period;
    }

    public static bool IsClosedOutOfSequence(PeriodClosingChainSnapshot snapshot, DateOnly period)
        => snapshot is { HasBrokenChain: true, NextClosablePeriod: not null }
           && period > snapshot.NextClosablePeriod.Value;
}
