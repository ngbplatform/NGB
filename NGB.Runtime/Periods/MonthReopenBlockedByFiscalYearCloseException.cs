using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class MonthReopenBlockedByFiscalYearCloseException(DateOnly period, Guid documentId)
    : NgbConflictException(
        message: $"Month {period:yyyy-MM-01} cannot be reopened because fiscal-year close is already recorded for this end period. documentId={documentId}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["period"] = period.ToString("yyyy-MM-dd"),
            ["documentId"] = documentId
        })
{
    public const string ErrorCodeConst = "period.month.reopen.fiscal_year_closed";

    public DateOnly Period { get; } = period;
    public Guid DocumentId { get; } = documentId;
}
