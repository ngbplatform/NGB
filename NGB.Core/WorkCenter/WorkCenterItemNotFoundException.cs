using NGB.Tools.Exceptions;

namespace NGB.Core.WorkCenter;

public sealed class WorkCenterItemNotFoundException(Guid itemId)
    : NgbNotFoundException(
        $"Work Center item '{itemId}' was not found.",
        "work_center.item_not_found",
        new Dictionary<string, object?> { ["itemId"] = itemId });
