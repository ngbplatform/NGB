using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.ReferenceRegisters;

/// <summary>
/// Helpers for computing the normalized bucket moment for periodic reference registers.
///
/// Bucket is always in UTC.
/// </summary>
public static class ReferenceRegisterPeriodBucket
{
    public static DateTime? ComputeUtc(DateTime? periodUtc, ReferenceRegisterPeriodicity periodicity)
    {
        if (periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
            return null;

        if (periodUtc is null)
            throw new NgbArgumentRequiredException(nameof(periodUtc));

        var p = periodUtc.Value;
        p.EnsureUtc(nameof(periodUtc));

        static DateTime Utc(DateTime dt) =>
            dt.Kind == DateTimeKind.Utc
                ? dt
                : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        return periodicity switch
        {
            ReferenceRegisterPeriodicity.Second => Utc(new DateTime(p.Year, p.Month, p.Day, p.Hour, p.Minute, p.Second, DateTimeKind.Utc)),
            ReferenceRegisterPeriodicity.Day => Utc(new DateTime(p.Year, p.Month, p.Day, 0, 0, 0, DateTimeKind.Utc)),
            ReferenceRegisterPeriodicity.Month => Utc(new DateTime(p.Year, p.Month, 1, 0, 0, 0, DateTimeKind.Utc)),
            ReferenceRegisterPeriodicity.Quarter => Utc(new DateTime(p.Year, QuarterStartMonth(p.Month), 1, 0, 0, 0, DateTimeKind.Utc)),
            ReferenceRegisterPeriodicity.Year => Utc(new DateTime(p.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            _ => throw new NgbArgumentOutOfRangeException(nameof(periodicity), periodicity, "Unsupported periodicity.")
        };
    }

    private static int QuarterStartMonth(int month)
    {
        // month 1..12
        if (month is < 1 or > 12)
            throw new NgbArgumentOutOfRangeException(nameof(month), month, "month must be in range 1..12.");

        return ((month - 1) / 3) * 3 + 1;
    }
}
