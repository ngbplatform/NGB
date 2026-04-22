using NGB.Tools.Exceptions;

namespace NGB.Tools.Extensions;

public static class DateOnlyExtensions
{
    public static void EnsureMonthStart(this DateOnly date, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new NgbArgumentRequiredException(nameof(name));

        if (date.Day != 1)
            throw new NgbArgumentOutOfRangeException(name, date, $"{NgbArgumentLabelFormatter.Format(name)} must be the first day of a month.");
    }
}
