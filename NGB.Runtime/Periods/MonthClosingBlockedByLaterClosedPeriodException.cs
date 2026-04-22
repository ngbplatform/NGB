using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class MonthClosingBlockedByLaterClosedPeriodException(DateOnly period, DateOnly latestClosedPeriod)
    : NgbConflictException(
        message: $"Month {period:yyyy-MM-01} cannot be closed because a later month is already closed. Reopen {latestClosedPeriod:yyyy-MM-01} first.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["period"] = period.ToString("yyyy-MM-dd"),
            ["latestClosedPeriod"] = latestClosedPeriod.ToString("yyyy-MM-dd")
        })
{
    public const string ErrorCodeConst = "period.month.later_closed_exists";

    public DateOnly Period { get; } = period;
    public DateOnly LatestClosedPeriod { get; } = latestClosedPeriod;
}
