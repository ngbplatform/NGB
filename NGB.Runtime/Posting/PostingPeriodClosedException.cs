using NGB.Tools.Exceptions;

namespace NGB.Runtime.Posting;

/// <summary>
/// Thrown when a posting pipeline attempts to write accounting movements into a closed accounting period.
/// </summary>
public sealed class PostingPeriodClosedException(string operation, DateOnly period) : NgbForbiddenException(
    message: $"Posting is forbidden. Period is closed: {period:yyyy-MM-dd}",
    errorCode: ErrorCodeConst,
    context: new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["operation"] = operation,
        ["period"] = period.ToString("yyyy-MM-dd")
    })
{
    public const string ErrorCodeConst = "accounting.posting.period_closed";

    public string Operation { get; } = operation;
    public DateOnly Period { get; } = period;
}
