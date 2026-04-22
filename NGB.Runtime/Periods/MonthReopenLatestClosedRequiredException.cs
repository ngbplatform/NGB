using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class MonthReopenLatestClosedRequiredException(DateOnly period, DateOnly latestClosedPeriod)
    : NgbConflictException(
        message: $"Only the latest closed month can be reopened. Requested={period:yyyy-MM-01}, latestClosed={latestClosedPeriod:yyyy-MM-01}.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["period"] = period.ToString("yyyy-MM-dd"),
            ["latestClosedPeriod"] = latestClosedPeriod.ToString("yyyy-MM-dd")
        })
{
    public const string ErrorCodeConst = "period.month.reopen.latest_closed_required";

    public DateOnly Period { get; } = period;
    public DateOnly LatestClosedPeriod { get; } = latestClosedPeriod;
}
