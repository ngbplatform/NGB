using NGB.Tools.Extensions;

namespace NGB.OperationalRegisters;

public static class OperationalRegisterPeriod
{
    public static DateOnly MonthStart(DateOnly date)
        => new(date.Year, date.Month, 1);

    public static DateOnly MonthStart(DateTime utc)
    {
        // Defensive: period calculations must be based on UTC timestamps.
        // Passing a non-UTC DateTime would produce subtle month drift bugs.
        utc.EnsureUtc(nameof(utc));

        var d = DateOnly.FromDateTime(utc);
        return MonthStart(d);
    }
}
