using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class FiscalYearClosingPrerequisiteNotMetException(DateOnly notClosedPeriod)
    : NgbConflictException(
        message: $"Fiscal year close requires all prior months to be closed. Not closed: {notClosedPeriod:yyyy-MM-01}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["notClosedPeriod"] = notClosedPeriod.ToString("yyyy-MM-dd")
        })
{
    public const string ErrorCodeConst = "period.fiscal_year.prerequisite_not_met";

    public DateOnly NotClosedPeriod { get; } = notClosedPeriod;
}
