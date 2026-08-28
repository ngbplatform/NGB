using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class OpenItemsResultLimitExceededException(int actualCount, int maximumCount)
    : NgbValidationException(
        message: $"Open Items contains {actualCount} rows, which exceeds the supported limit of {maximumCount}. Narrow the business context or use a paged report.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["actualCount"] = actualCount,
            ["maximumCount"] = maximumCount
        })
{
    public const string Code = "pm.open_items.result_limit_exceeded";
}
