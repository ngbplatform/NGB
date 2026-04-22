using NGB.Tools.Exceptions;

namespace NGB.Tools.Extensions;

public static class DateTimeExtensions
{
    public static void EnsureUtc(this DateTime dt, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new NgbArgumentRequiredException(nameof(name));

        if (dt.Kind != DateTimeKind.Utc)
            throw new NgbArgumentInvalidException(name, $"{name} must be UTC (DateTimeKind.Utc). Actual kind: {dt.Kind}.");
    }
}
