using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class FiscalYearClosingAlreadyInProgressException(DateOnly fiscalYearEndPeriod, Guid documentId)
    : NgbConflictException(
        message: $"Fiscal year closing is already in progress. endPeriod={fiscalYearEndPeriod:yyyy-MM-dd} documentId={documentId}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["fiscalYearEndPeriod"] = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
            ["documentId"] = documentId
        })
{
    public const string ErrorCodeConst = "period.fiscal_year.in_progress";

    public DateOnly FiscalYearEndPeriod { get; } = fiscalYearEndPeriod;
    public Guid DocumentId { get; } = documentId;
}
