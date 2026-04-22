using NGB.Tools.Exceptions;

namespace NGB.Accounting.Periods;

/// <summary>
/// Thrown when an operation requires an open accounting period, but the period is already closed.
/// </summary>
public sealed class PeriodAlreadyClosedException(DateOnly period) : NgbConflictException(
    message: $"Period is already closed: {AccountingPeriod.FromDateOnly(period):yyyy-MM-dd}",
    errorCode: ErrorCodeConst,
    context: new Dictionary<string, object?>
    {
        ["period"] = AccountingPeriod.FromDateOnly(period),
    })
{
    public const string ErrorCodeConst = "period.already_closed";

    public DateOnly Period { get; } = AccountingPeriod.FromDateOnly(period);
}
