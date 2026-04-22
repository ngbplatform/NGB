using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class FiscalYearClosingBlockedByLaterClosedPeriodException(
    DateOnly fiscalYearEndPeriod,
    DateOnly latestClosedPeriod)
    : NgbConflictException(
        message: $"Fiscal year close for {fiscalYearEndPeriod:yyyy-MM-01} is blocked because a later month is already closed. Reopen {latestClosedPeriod:yyyy-MM-01} first.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["fiscalYearEndPeriod"] = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
            ["latestClosedPeriod"] = latestClosedPeriod.ToString("yyyy-MM-dd")
        })
{
    public const string ErrorCodeConst = "period.fiscal_year.later_closed_exists";

    public DateOnly FiscalYearEndPeriod { get; } = fiscalYearEndPeriod;
    public DateOnly LatestClosedPeriod { get; } = latestClosedPeriod;
}
