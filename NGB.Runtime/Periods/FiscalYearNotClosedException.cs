using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class FiscalYearNotClosedException(DateOnly fiscalYearEndPeriod, Guid documentId)
    : NgbConflictException(
        message: $"Fiscal year is not currently closed for end period {fiscalYearEndPeriod:yyyy-MM-dd}. documentId={documentId}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["fiscalYearEndPeriod"] = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
            ["documentId"] = documentId
        })
{
    public const string ErrorCodeConst = "period.fiscal_year.not_closed";

    public DateOnly FiscalYearEndPeriod { get; } = fiscalYearEndPeriod;
    public Guid DocumentId { get; } = documentId;
}
