using NGB.Tools.Exceptions;

namespace NGB.Runtime.Maintenance;

/// <summary>
/// Thrown when an accounting rebuild is requested for a period that is already closed.
/// </summary>
public sealed class AccountingRebuildPeriodClosedException(DateOnly period) : NgbForbiddenException(
    message: $"Rebuild is forbidden. Period is closed: {period:yyyy-MM-dd}",
    errorCode: ErrorCodeConst,
    context: new Dictionary<string, object?>
    {
        ["period"] = period.ToString("yyyy-MM-dd")
    })
{
    public const string ErrorCodeConst = "accounting.rebuild.period_closed";

    public DateOnly Period { get; } = period;
}
