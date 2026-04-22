using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class FiscalYearAlreadyClosedWithDifferentRetainedEarningsException(
    DateOnly fiscalYearEndPeriod,
    Guid documentId,
    Guid requestedRetainedEarningsAccountId,
    Guid actualRetainedEarningsAccountId,
    string? actualRetainedEarningsAccountDisplay = null)
    : NgbConflictException(
        message: string.IsNullOrWhiteSpace(actualRetainedEarningsAccountDisplay)
            ? $"Fiscal year is already closed for end period {fiscalYearEndPeriod:yyyy-MM-dd} with a different retained earnings account. documentId={documentId}"
            : $"Fiscal year is already closed for end period {fiscalYearEndPeriod:yyyy-MM-dd} using retained earnings account '{actualRetainedEarningsAccountDisplay}'. documentId={documentId}",
        errorCode: ErrorCodeConst,
        context: BuildContext(
            fiscalYearEndPeriod,
            documentId,
            requestedRetainedEarningsAccountId,
            actualRetainedEarningsAccountId,
            actualRetainedEarningsAccountDisplay))
{
    public const string ErrorCodeConst = "period.fiscal_year.retained_earnings_mismatch";

    public DateOnly FiscalYearEndPeriod { get; } = fiscalYearEndPeriod;
    public Guid DocumentId { get; } = documentId;
    public Guid RequestedRetainedEarningsAccountId { get; } = requestedRetainedEarningsAccountId;
    public Guid ActualRetainedEarningsAccountId { get; } = actualRetainedEarningsAccountId;
    public string? ActualRetainedEarningsAccountDisplay { get; } = actualRetainedEarningsAccountDisplay;

    private static IReadOnlyDictionary<string, object?> BuildContext(
        DateOnly fiscalYearEndPeriod,
        Guid documentId,
        Guid requestedRetainedEarningsAccountId,
        Guid actualRetainedEarningsAccountId,
        string? actualRetainedEarningsAccountDisplay)
    {
        var ctx = new Dictionary<string, object?>
        {
            ["fiscalYearEndPeriod"] = fiscalYearEndPeriod.ToString("yyyy-MM-dd"),
            ["documentId"] = documentId,
            ["requestedRetainedEarningsAccountId"] = requestedRetainedEarningsAccountId,
            ["actualRetainedEarningsAccountId"] = actualRetainedEarningsAccountId
        };

        if (!string.IsNullOrWhiteSpace(actualRetainedEarningsAccountDisplay))
            ctx["actualRetainedEarningsAccountDisplay"] = actualRetainedEarningsAccountDisplay;

        return ctx;
    }
}
