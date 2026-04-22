using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class MonthClosingPrerequisiteNotMetException(DateOnly nextClosablePeriod)
    : NgbConflictException(
        message: $"Month close requires previous months to be closed first. Next closable period: {nextClosablePeriod:yyyy-MM-01}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["nextClosablePeriod"] = nextClosablePeriod.ToString("yyyy-MM-dd")
        })
{
    public const string ErrorCodeConst = "period.month.prerequisite_not_met";

    public DateOnly NextClosablePeriod { get; } = nextClosablePeriod;
}
