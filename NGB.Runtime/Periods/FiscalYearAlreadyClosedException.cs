using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class FiscalYearAlreadyClosedException(DateOnly fiscalYearEndPeriod, Guid documentId)
    : NgbConflictException(
        message: $"Fiscal year is already closed for end period {fiscalYearEndPeriod:yyyy-MM-dd}. documentId={documentId}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["fiscalYearEndPeriod"] = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
            ["documentId"] = documentId
        })
{
    public const string ErrorCodeConst = "period.fiscal_year.already_closed";

    public DateOnly FiscalYearEndPeriod { get; } = fiscalYearEndPeriod;
    public Guid DocumentId { get; } = documentId;
}
