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

        return periodicity switch
        {
            ReferenceRegisterPeriodicity.Second => new DateTime(p.Year, p.Month, p.Day, p.Hour, p.Minute, p.Second, DateTimeKind.Utc),
            ReferenceRegisterPeriodicity.Day => new DateTime(p.Year, p.Month, p.Day, 0, 0, 0, DateTimeKind.Utc),
            ReferenceRegisterPeriodicity.Month => new DateTime(p.Year, p.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferenceRegisterPeriodicity.Quarter => new DateTime(p.Year, QuarterStartMonth(p.Month), 1, 0, 0, 0, DateTimeKind.Utc),
            ReferenceRegisterPeriodicity.Year => new DateTime(p.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => throw new NgbArgumentOutOfRangeException(nameof(periodicity), periodicity, "Unsupported periodicity.")
        };
    }

    private static int QuarterStartMonth(int month) => ((month - 1) / 3) * 3 + 1;
}
