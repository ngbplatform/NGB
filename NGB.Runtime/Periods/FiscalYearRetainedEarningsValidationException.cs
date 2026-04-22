using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class FiscalYearRetainedEarningsValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static FiscalYearRetainedEarningsValidationException DimensionsNotAllowed(
        Guid retainedEarningsAccountId,
        string accountCode)
    {
        const string message = "Retained earnings account must not require dimensions.";
        return new(
            message,
            "period.fiscal_year.retained_earnings_dimensions_not_allowed",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["retainedEarningsAccountId"] = retainedEarningsAccountId,
                ["accountCode"] = accountCode,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["retainedEarningsAccountId"] = [message]
                }
            });
    }
}
