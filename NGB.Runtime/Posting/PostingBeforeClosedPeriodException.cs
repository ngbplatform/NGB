using NGB.Tools.Exceptions;

namespace NGB.Runtime.Posting;

public sealed class PostingBeforeClosedPeriodException(string operation, DateOnly period, DateOnly closedPeriod)
    : NgbForbiddenException(
        message: $"Posting into {period:yyyy-MM-dd} is forbidden because a later period is closed: {closedPeriod:yyyy-MM-dd}. Reopen the dependent closed periods first.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["period"] = period.ToString("yyyy-MM-dd"),
            ["closedPeriod"] = closedPeriod.ToString("yyyy-MM-dd")
        })
{
    public const string ErrorCodeConst = "accounting.posting.later_period_closed";

    public string Operation { get; } = operation;
    public DateOnly Period { get; } = period;
    public DateOnly ClosedPeriod { get; } = closedPeriod;
}
