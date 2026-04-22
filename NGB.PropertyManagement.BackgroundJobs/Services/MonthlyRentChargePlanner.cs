namespace NGB.PropertyManagement.BackgroundJobs.Services;

internal static class MonthlyRentChargePlanner
{
    public static IReadOnlyList<MonthlyRentChargeCandidate> BuildCandidates(
        PmRentChargeGenerationLease lease,
        DateOnly asOfUtc)
    {
        if (lease.RentAmount <= 0m)
            return [];

        if (lease.StartOnUtc > asOfUtc)
            return [];

        var effectiveLeaseEnd = lease.EndOnUtc is { } end && end < asOfUtc
            ? end
            : asOfUtc;

        if (effectiveLeaseEnd < lease.StartOnUtc)
            return [];

        var month = new DateOnly(lease.StartOnUtc.Year, lease.StartOnUtc.Month, 1);
        var lastMonth = new DateOnly(effectiveLeaseEnd.Year, effectiveLeaseEnd.Month, 1);
        var candidates = new List<MonthlyRentChargeCandidate>();

        while (month <= lastMonth)
        {
            var monthStart = month;
            var monthEnd = month.AddMonths(1).AddDays(-1);
            var periodFrom = lease.StartOnUtc > monthStart ? lease.StartOnUtc : monthStart;
            var periodTo = effectiveLeaseEnd < monthEnd ? effectiveLeaseEnd : monthEnd;

            if (periodFrom <= periodTo)
            {
                var dueOnUtc = ResolveDueDate(month, periodFrom, lease.DueDay);
                if (dueOnUtc <= asOfUtc)
                {
                    candidates.Add(new MonthlyRentChargeCandidate(
                        lease.LeaseId,
                        periodFrom,
                        periodTo,
                        dueOnUtc,
                        lease.RentAmount,
                        $"Monthly rent for {periodFrom:MMMM yyyy}."));
                }
            }

            month = month.AddMonths(1);
        }

        return candidates;
    }

    private static DateOnly ResolveDueDate(DateOnly month, DateOnly periodFromUtc, int? dueDay)
    {
        var normalizedDueDay = dueDay is null
            ? periodFromUtc.Day
            : Math.Clamp(dueDay.Value, 1, 31);

        var lastDayOfMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var nominalDueDate = new DateOnly(month.Year, month.Month, Math.Min(normalizedDueDay, lastDayOfMonth));

        return nominalDueDate < periodFromUtc
            ? periodFromUtc
            : nominalDueDate;
    }
}

internal sealed record MonthlyRentChargeCandidate(
    Guid LeaseId,
    DateOnly PeriodFromUtc,
    DateOnly PeriodToUtc,
    DateOnly DueOnUtc,
    decimal Amount,
    string Memo);
