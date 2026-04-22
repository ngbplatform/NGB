using NGB.Tools.Exceptions;

namespace NGB.Tools.Extensions;

public static class TimeProviderExtensions
{
    public static DateTime GetUtcNowDateTime(this TimeProvider timeProvider)
    {
        if (timeProvider is null)
            throw new NgbArgumentRequiredException(nameof(timeProvider));

        return timeProvider.GetUtcNow().UtcDateTime;
    }

    public static DateOnly GetUtcToday(this TimeProvider timeProvider)
        => DateOnly.FromDateTime(timeProvider.GetUtcNowDateTime());
}
