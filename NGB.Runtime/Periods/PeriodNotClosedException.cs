using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class PeriodNotClosedException(DateOnly period)
    : NgbConflictException(
        message: $"Period is not closed: {period:yyyy-MM-dd}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["period"] = period.ToString("yyyy-MM-dd")
        })
{
    public const string ErrorCodeConst = "period.not_closed";

    public DateOnly Period { get; } = period;
}
