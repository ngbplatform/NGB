namespace NGB.BackgroundJobs.Jobs;

internal static class JobPeriods
{
    public static DateOnly CurrentMonthStartUtc(DateTime nowUtc)
    {
        return new DateOnly(nowUtc.Year, nowUtc.Month, 1);
    }

    public static DateOnly AddMonths(DateOnly monthStart, int months) => monthStart.AddMonths(months);
}
