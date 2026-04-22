using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class FiscalYearReopenBlockedByInProgressException(DateOnly fiscalYearEndPeriod, Guid documentId)
    : NgbConflictException(
        message: $"Fiscal year reopen is blocked because close state is still in progress. endPeriod={fiscalYearEndPeriod:yyyy-MM-dd} documentId={documentId}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["fiscalYearEndPeriod"] = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
            ["documentId"] = documentId
        })
{
    public const string ErrorCodeConst = "period.fiscal_year.reopen.in_progress";

    public DateOnly FiscalYearEndPeriod { get; } = fiscalYearEndPeriod;
    public Guid DocumentId { get; } = documentId;
}
