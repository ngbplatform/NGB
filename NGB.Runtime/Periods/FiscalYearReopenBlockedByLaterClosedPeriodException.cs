using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class FiscalYearReopenBlockedByLaterClosedPeriodException(
    DateOnly fiscalYearEndPeriod,
    DateOnly latestClosedPeriod)
    : NgbConflictException(
        message: $"Fiscal year reopen is blocked because a later month is already closed. endPeriod={fiscalYearEndPeriod:yyyy-MM-dd} latestClosedPeriod={latestClosedPeriod:yyyy-MM-dd}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["fiscalYearEndPeriod"] = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
            ["latestClosedPeriod"] = latestClosedPeriod.ToString("yyyy-MM-dd")
        })
{
    public const string ErrorCodeConst = "period.fiscal_year.reopen.later_closed_exists";

    public DateOnly FiscalYearEndPeriod { get; } = fiscalYearEndPeriod;
    public DateOnly LatestClosedPeriod { get; } = latestClosedPeriod;
}
